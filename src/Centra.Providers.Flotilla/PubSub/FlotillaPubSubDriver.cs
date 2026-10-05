using System.Collections.Concurrent;
using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Protocol;
using Centra.PubSub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centra.Providers.Flotilla.PubSub;

/// <summary>
/// Centra PubSub driver backed by Flotilla Raft consensus engine.
/// </summary>
public sealed class FlotillaPubSubDriver : IPubSubDriver, IPubSubShutdownDrain, IDisposable, IAsyncDisposable
{
    private readonly IFlotillaClient _client;
    private readonly ILogger<FlotillaPubSubDriver> _logger;
    private readonly ConcurrentDictionary<string, List<FlotillaSubscription>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _drainCts = new();
    private int _disposed;

    public FlotillaPubSubDriver(
        IFlotillaClient client,
        ILogger<FlotillaPubSubDriver>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? NullLogger<FlotillaPubSubDriver>.Instance;
    }

    public async ValueTask PublishAsync(
        string pubSubName,
        string topic,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        ActivityContext parentContext = default;
        if (metadata != null && metadata.TryGetValue("traceparent", out var tp) && !string.IsNullOrWhiteSpace(tp))
        {
            var ts = metadata.TryGetValue("tracestate", out var s) ? s : null;
            ActivityContext.TryParse(tp, ts, out parentContext);
        }

        using var activity = parentContext != default && Activity.Current == null
            ? CentraDiagnostics.StartPublishActivity(pubSubName, topic, parentContext)
            : null;

        var encoded = FlotillaWireProtocol.EncodeMessage(topic, metadata, payload.Span);
        var result = await _client.ProposeAsync(encoded, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Flotilla proposal failed: {result.ErrorMessage}");
        }
    }

    public async ValueTask PublishBatchAsync(
        string pubSubName,
        string topic,
        IReadOnlyList<PubSubMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var msg in messages)
        {
            await PublishAsync(
                pubSubName,
                topic,
                msg.Payload,
                msg.Metadata ?? new Dictionary<string, string>(),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask SubscribeAsync(
        string pubSubName,
        string topic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        string? deadLetterTopic = null,
        CancellationToken cancellationToken = default,
        PubSubSubscribeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);
        RedeliveryBudgetOptionsGuard.ThrowIfConfigured(options, nameof(FlotillaPubSubDriver));

        var sub = new FlotillaSubscription(topic, handler, deadLetterTopic, options);
        var list = _subscriptions.GetOrAdd(topic, _ => new List<FlotillaSubscription>());
        lock (list)
        {
            list.Add(sub);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(
        string pubSubName,
        string topic,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        _subscriptions.TryRemove(topic, out _);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Gets all active subscriptions registered for the specified topic.
    /// </summary>
    public IReadOnlyList<FlotillaSubscription> GetSubscriptions(string topic)
    {
        if (_subscriptions.TryGetValue(topic, out var list))
        {
            lock (list)
            {
                return list.ToArray();
            }
        }
        return Array.Empty<FlotillaSubscription>();
    }

    public IDisposable BeginShutdownDrain()
    {
        _drainCts.Cancel();
        return new FlotillaDrainDisposable();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _drainCts.Cancel();
        _drainCts.Dispose();
        _subscriptions.Clear();
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}
