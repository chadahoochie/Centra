using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Events;
using Centra.Hosting.Routing;
using Centra.PubSub;
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

        string? causeId = null;
        string? tenantId = null;

        if (headers.TryGetValue(CloudEventConstants.CorrelationIdHeader, out var corrId))
        {
            CentraAmbientContext.CorrelationId = corrId;
        }

        if (headers.TryGetValue(CloudEventConstants.IdHeader, out causeId))
        {
            CentraAmbientContext.CausationId = causeId;
        }

        if (headers.TryGetValue(CloudEventConstants.TenantIdHeader, out tenantId))
        {
            CentraAmbientContext.TenantId = tenantId;
        }

        var offloadCoordinator = _serviceProvider.GetService<Centra.PubSub.Tenancy.ITenantOffloadCoordinator>();

        try
        {
            if (offloadCoordinator is not null && !string.IsNullOrWhiteSpace(tenantId) && !headers.ContainsKey("ce-offloaded"))
            {
                if (offloadCoordinator.IsTenantOffloaded(tenantId, reg.Topic, out _))
                {
                    var workItem = new Centra.PubSub.Tenancy.TenantOffloadWorkItem(
                        tenantId,
                        reg.PubSubName,
                        reg.Topic,
                        payload,
                        headers,
                        ct => CentraSubscriptionPipelineExecutor.ExecuteAsync(_serviceProvider, reg, payload, headers, causeId, ct),
                        DateTimeOffset.UtcNow);

                    var offloadResult = await offloadCoordinator.HandleOffloadAsync(workItem, cancellationToken).ConfigureAwait(false);
                    var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                    CentraMeters.RecordPubSubConsumed(reg.PubSubName, reg.Topic, offloadResult.ToString(), durationMs);
                    return offloadResult;
                }
            }

            var result = await CentraSubscriptionPipelineExecutor.ExecuteAsync(_serviceProvider, reg, payload, headers, causeId, cancellationToken).ConfigureAwait(false);

            var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            if (offloadCoordinator is not null && !string.IsNullOrWhiteSpace(tenantId))
            {
                offloadCoordinator.RecordExecution(tenantId, reg.Topic, elapsedMs);
            }

            CentraMeters.RecordPubSubConsumed(reg.PubSubName, reg.Topic, result.ToString(), elapsedMs);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            if (offloadCoordinator is not null && !string.IsNullOrWhiteSpace(tenantId))
            {
                offloadCoordinator.RecordExecution(tenantId, reg.Topic, durationMs);
            }

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
