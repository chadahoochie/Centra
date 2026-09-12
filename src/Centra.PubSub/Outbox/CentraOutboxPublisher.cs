using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Events;
using Centra.PubSub.Outbox;

namespace Centra.PubSub.Outbox;

/// <summary>
/// Default implementation of <see cref="IOutboxPublisher"/> wrapping domain events into CloudEvents
/// and persisting them into an <see cref="IOutboxStore"/>.
/// </summary>
public sealed class CentraOutboxPublisher : IOutboxPublisher
{
    private readonly IOutboxStore _outboxStore;
    private readonly string _defaultPubSubName;
    private readonly string _appId;

    public CentraOutboxPublisher(
        IOutboxStore outboxStore,
        string defaultPubSubName = "pubsub",
        string? appId = null)
    {
        _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        _defaultPubSubName = defaultPubSubName;
        _appId = appId ?? "centra-app";
    }

    public ValueTask EnqueueAsync<T>(
        string topic,
        T data,
        string? pubSubName = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var packed = CloudEventPacker.Pack(data, _appId, CloudEventMode.Binary, subject: null, additionalMetadata: headers);
        var mutableHeaders = new Dictionary<string, string>(packed.Headers, StringComparer.OrdinalIgnoreCase);

        // Inject active trace context into headers
        CentraTracePropagator.Inject(Activity.Current, mutableHeaders);

        var resolvedPubSub = !string.IsNullOrWhiteSpace(pubSubName) ? pubSubName : _defaultPubSubName;
        var messageId = packed.Headers.TryGetValue(CloudEventConstants.IdHeader, out var id) ? id : Guid.NewGuid().ToString("N");

        var outboxMessage = new OutboxMessage(
            messageId,
            resolvedPubSub,
            topic,
            packed.Payload,
            mutableHeaders,
            DateTimeOffset.UtcNow);

        return _outboxStore.EnqueueAsync(outboxMessage, cancellationToken);
    }

    public ValueTask EnqueueRawAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        string? pubSubName = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var mutableHeaders = headers != null
            ? new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        CentraTracePropagator.Inject(Activity.Current, mutableHeaders);

        var resolvedPubSub = !string.IsNullOrWhiteSpace(pubSubName) ? pubSubName : _defaultPubSubName;
        var messageId = mutableHeaders.TryGetValue(CloudEventConstants.IdHeader, out var id) ? id : Guid.NewGuid().ToString("N");

        var outboxMessage = new OutboxMessage(
            messageId,
            resolvedPubSub,
            topic,
            payload,
            mutableHeaders,
            DateTimeOffset.UtcNow);

        return _outboxStore.EnqueueAsync(outboxMessage, cancellationToken);
    }
}
