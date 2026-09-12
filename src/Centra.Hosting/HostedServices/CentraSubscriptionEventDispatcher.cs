using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Events;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Centra.PubSub.Inbox;
using Centra.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centra.Hosting.HostedServices;

internal sealed class CentraSubscriptionEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    public CentraSubscriptionEventDispatcher(IServiceProvider serviceProvider, ILogger logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<EventHandlingResult> DispatchEventAsync(
        CentraTopicRegistration reg,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var parentContext = CentraTracePropagator.Extract(headers);
        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartProcessActivity(reg.PubSubName, reg.Topic, parentContext);

        if (headers.TryGetValue(CloudEventConstants.CorrelationIdHeader, out var corrId))
        {
            CentraAmbientContext.CorrelationId = corrId;
        }

        if (headers.TryGetValue(CloudEventConstants.IdHeader, out var causeId))
        {
            CentraAmbientContext.CausationId = causeId;
        }

        if (headers.TryGetValue(CloudEventConstants.TenantIdHeader, out var tenantId))
        {
            CentraAmbientContext.TenantId = tenantId;
        }

        try
        {
            var inboxStore = _serviceProvider.GetService<IInboxStore>();
            var messageId = causeId ?? (headers.TryGetValue(CloudEventConstants.IdHeader, out var id) ? id : null);
            var consumerId = reg.HandlerType.FullName ?? reg.HandlerType.Name;

            if (inboxStore is not null && !string.IsNullOrWhiteSpace(messageId))
            {
                if (await inboxStore.HasBeenProcessedAsync(messageId, consumerId, cancellationToken).ConfigureAwait(false))
                {
                    CentraMeters.RecordPubSubConsumed(reg.PubSubName, reg.Topic, "duplicate_skipped", 0);
                    return EventHandlingResult.Success;
                }
            }

            using var scope = _serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetService(reg.HandlerType);
            if (handler is null)
            {
                return EventHandlingResult.Drop;
            }

            var resilienceProvider = _serviceProvider.GetService<IResiliencePipelineProvider>();
            EventHandlingResult result;
            if (resilienceProvider is not null)
            {
                var pipeline = resilienceProvider.GetPubSubPipeline(reg.PubSubName);
                result = await pipeline.ExecuteAsync(async ct =>
                {
                    return await reg.Invoker(handler, payload, headers, ct).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await reg.Invoker(handler, payload, headers, cancellationToken).ConfigureAwait(false);
            }

            if (inboxStore is not null && !string.IsNullOrWhiteSpace(messageId) && result == EventHandlingResult.Success)
            {
                await inboxStore.MarkProcessedAsync(messageId, consumerId, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordPubSubConsumed(reg.PubSubName, reg.Topic, result.ToString(), durationMs);

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordPubSubConsumed(reg.PubSubName, reg.Topic, "error", durationMs);
            _logger.LogEventProcessingFailed(ex, reg.EventType.Name, reg.PubSubName, reg.Topic, causeId ?? "unknown");
            return EventHandlingResult.DeadLetter;
        }
        finally
        {
            CentraAmbientContext.Clear();
        }
    }
}
