using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Centra.Drivers;
using Centra.Events;
using Centra.Providers.AzureServiceBus.Options;
using Centra.PubSub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Centra.Providers.AzureServiceBus.PubSub;

public sealed class AzureServiceBusPubSubDriver : IPubSubDriver, IAsyncDisposable, IDisposable
{
    private readonly ServiceBusClient _client;
    private readonly AzureServiceBusProviderOptions _options;
    private readonly ILogger<AzureServiceBusPubSubDriver> _logger;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ServiceBusProcessor> _processors = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ServiceBusSessionProcessor> _sessionProcessors = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _ownsClient;
    private int _disposed;

    public AzureServiceBusPubSubDriver(
        IOptions<AzureServiceBusProviderOptions> options,
        ILogger<AzureServiceBusPubSubDriver>? logger = null)
        : this(new ServiceBusClient(options?.Value.ConnectionString ?? throw new ArgumentNullException(nameof(options))), options, ownsClient: true, logger)
    {
    }

    public AzureServiceBusPubSubDriver(
        ServiceBusClient client,
        IOptions<AzureServiceBusProviderOptions> options,
        bool ownsClient = false,
        ILogger<AzureServiceBusPubSubDriver>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? new AzureServiceBusProviderOptions();
        _logger = logger ?? NullLogger<AzureServiceBusPubSubDriver>.Instance;
        _ownsClient = ownsClient;
    }

    private ServiceBusSender GetSender(string topic)
    {
        var targetTopic = $"{_options.TopicPrefix}{topic}";
        return _senders.GetOrAdd(targetTopic, t => _client.CreateSender(t));
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

        var sender = GetSender(topic);
        var message = new ServiceBusMessage(new BinaryData(payload));

        if (metadata.Count > 0)
        {
            foreach (var (k, v) in metadata)
            {
                message.ApplicationProperties[k] = v;

                if (string.Equals(k, CloudEventConstants.IdHeader, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(k, CloudEventConstants.IdAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    message.MessageId = v;
                }
                else if (string.Equals(k, CloudEventConstants.SubjectHeader, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(k, CloudEventConstants.SubjectAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    message.Subject = v;
                }
                else if (string.Equals(k, CloudEventConstants.CorrelationIdHeader, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(k, CloudEventConstants.CorrelationIdAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    message.CorrelationId = v;
                }
                else if (string.Equals(k, CloudEventConstants.DataContentTypeHeader, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(k, CloudEventConstants.DataContentTypeAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    message.ContentType = v;
                }
            }
        }

        await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SubscribeAsync(
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

        var targetTopic = $"{_options.TopicPrefix}{topic}";
        var subKey = $"{pubSubName}:{targetTopic}";

        if (options?.ConsumerMode == ConsumerMode.SingleActiveConsumer)
        {
            // Requires the subscription to be provisioned with RequiresSession=true - an
            // infrastructure/provisioning concern, not something this driver can set at runtime.
            var sessionProcessorOptions = new ServiceBusSessionProcessorOptions
            {
                AutoCompleteMessages = _options.AutoCompleteMessages,
                MaxConcurrentSessions = 1,
                SessionIds = { _options.SingleActiveSessionId }
            };

            var sessionProcessor = _client.CreateSessionProcessor(targetTopic, _options.SubscriptionName, sessionProcessorOptions);

            sessionProcessor.ProcessMessageAsync += async args =>
            {
                try
                {
                    var headers = ExtractHeaders(args.Message);
                    var result = await handler(args.Message.Body.ToMemory(), headers, args.CancellationToken).ConfigureAwait(false);
                    await SettleSessionMessageAsync(args, result).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing Azure Service Bus session message {MessageId} on topic {Topic}", args.Message.MessageId, targetTopic);
                    await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
                }
            };

            sessionProcessor.ProcessErrorAsync += args =>
            {
                _logger.LogError(args.Exception, "Service Bus session processor error on entity {EntityPath}: {ErrorSource}", args.EntityPath, args.ErrorSource);
                return Task.CompletedTask;
            };

            await sessionProcessor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
            _sessionProcessors[subKey] = sessionProcessor;
            return;
        }

        var processorOptions = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = _options.AutoCompleteMessages,
            MaxConcurrentCalls = _options.MaxConcurrentCalls
        };

        var processor = _client.CreateProcessor(targetTopic, _options.SubscriptionName, processorOptions);

        processor.ProcessMessageAsync += async args =>
        {
            try
            {
                var headers = ExtractHeaders(args.Message);
                var result = await handler(args.Message.Body.ToMemory(), headers, args.CancellationToken).ConfigureAwait(false);
                await SettleMessageAsync(args, result).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Azure Service Bus message {MessageId} on topic {Topic}", args.Message.MessageId, targetTopic);
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
            }
        };

        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Service Bus processor error on entity {EntityPath}: {ErrorSource}", args.EntityPath, args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);
        _processors[subKey] = processor;
    }

    public async ValueTask UnsubscribeAsync(
        string pubSubName,
        string topic,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var targetTopic = $"{_options.TopicPrefix}{topic}";
        var subKey = $"{pubSubName}:{targetTopic}";

        if (_processors.TryRemove(subKey, out var processor))
        {
            await processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
            await processor.DisposeAsync().ConfigureAwait(false);
        }

        if (_sessionProcessors.TryRemove(subKey, out var sessionProcessor))
        {
            await sessionProcessor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
            await sessionProcessor.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var (_, processor) in _processors)
        {
            try
            {
                await processor.StopProcessingAsync().ConfigureAwait(false);
                await processor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error stopping Service Bus processor during disposal.");
            }
        }
        _processors.Clear();

        foreach (var (_, sessionProcessor) in _sessionProcessors)
        {
            try
            {
                await sessionProcessor.StopProcessingAsync().ConfigureAwait(false);
                await sessionProcessor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error stopping Service Bus session processor during disposal.");
            }
        }
        _sessionProcessors.Clear();

        foreach (var (_, sender) in _senders)
        {
            try
            {
                await sender.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing Service Bus sender during disposal.");
            }
        }
        _senders.Clear();

        if (_ownsClient)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static Dictionary<string, string> ExtractHeaders(ServiceBusReceivedMessage message)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (k, v) in message.ApplicationProperties)
        {
            if (v is not null)
            {
                headers[k] = v.ToString()!;
            }
        }

        CopyHeaderIfPresent(headers, CloudEventConstants.IdHeader, message.MessageId);
        CopyHeaderIfPresent(headers, CloudEventConstants.SubjectHeader, message.Subject);
        CopyHeaderIfPresent(headers, CloudEventConstants.CorrelationIdHeader, message.CorrelationId);
        CopyHeaderIfPresent(headers, CloudEventConstants.DataContentTypeHeader, message.ContentType);

        return headers;
    }

    private static void CopyHeaderIfPresent(Dictionary<string, string> headers, string headerName, string? value)
    {
        if (!string.IsNullOrEmpty(value) && !headers.ContainsKey(headerName))
        {
            headers[headerName] = value;
        }
    }

    private static Task SettleMessageAsync(ProcessMessageEventArgs args, EventHandlingResult result)
    {
        return result switch
        {
            EventHandlingResult.Success or EventHandlingResult.Drop =>
                args.CompleteMessageAsync(args.Message, args.CancellationToken),

            EventHandlingResult.DeadLetter =>
                args.DeadLetterMessageAsync(args.Message, "DeadLetter", "Handler requested dead-lettering", args.CancellationToken),

            _ => args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken)
        };
    }

    private static Task SettleSessionMessageAsync(ProcessSessionMessageEventArgs args, EventHandlingResult result)
    {
        return result switch
        {
            EventHandlingResult.Success or EventHandlingResult.Drop =>
                args.CompleteMessageAsync(args.Message, args.CancellationToken),

            EventHandlingResult.DeadLetter =>
                args.DeadLetterMessageAsync(args.Message, "DeadLetter", "Handler requested dead-lettering", args.CancellationToken),

            _ => args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken)
        };
    }
}
