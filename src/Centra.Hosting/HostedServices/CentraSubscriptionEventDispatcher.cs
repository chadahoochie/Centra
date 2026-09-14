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
    private readonly IInboxStore? _inboxStore;
    private readonly IResiliencePipelineProvider? _resilienceProvider;

    public CentraSubscriptionEventDispatcher(IServiceProvider serviceProvider, ILogger logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inboxStore = serviceProvider.GetService<IInboxStore>();
        _resilienceProvider = serviceProvider.GetService<IResiliencePipelineProvider>();
    }

    public async ValueTask<EventHandlingResult> DispatchEventAsync(
        CentraTopicRouter router,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var context = CentraEventContextExtractor.Extract(router.PubSubName, router.Topic, headers);
        var matchingRoute = router.SelectRoute(payload, headers, in context);

        if (matchingRoute is null)
        {
            CentraMeters.RecordPubSubConsumed(router.PubSubName, router.Topic, "unrouted_drop", 0);
            return EventHandlingResult.Success;
        }

        return await DispatchEventAsync(matchingRoute, payload, headers, cancellationToken).ConfigureAwait(false);
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
            var messageId = causeId ?? (headers.TryGetValue(CloudEventConstants.IdHeader, out var id) ? id : null);
            var consumerId = reg.HandlerType.FullName ?? reg.HandlerType.Name;

            if (_inboxStore is not null && !string.IsNullOrWhiteSpace(messageId))
            {
                if (await _inboxStore.HasBeenProcessedAsync(messageId, consumerId, cancellationToken).ConfigureAwait(false))
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

            EventHandlingResult result;
            if (_resilienceProvider is not null)
            {
                var pipeline = _resilienceProvider.GetPubSubPipeline(reg.PubSubName);
                result = await pipeline.ExecuteAsync(async ct =>
                {
                    return await reg.Invoker(handler, payload, headers, ct).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await reg.Invoker(handler, payload, headers, cancellationToken).ConfigureAwait(false);
            }

            if (_inboxStore is not null && !string.IsNullOrWhiteSpace(messageId) && result == EventHandlingResult.Success)
            {
                await _inboxStore.MarkProcessedAsync(messageId, consumerId, cancellationToken: cancellationToken).ConfigureAwait(false);
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
