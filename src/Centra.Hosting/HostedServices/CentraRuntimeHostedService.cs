using System.Diagnostics;
using System.Reflection;
using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Events;
using Centra.Hosting.Options;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Centra.Registry;
using Centra.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.HostedServices;

public sealed class CentraRuntimeHostedService : IHostedService
{
    private readonly ComponentRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<CentraOptions> _options;
    private readonly IReadOnlyList<CentraTopicRegistration> _registrations;
    private readonly ILogger<CentraRuntimeHostedService> _logger;

    public CentraRuntimeHostedService(
        ComponentRegistry registry,
        IServiceProvider serviceProvider,
        IOptions<CentraOptions> options,
        IEnumerable<CentraTopicRegistration> registrations,
        ILogger<CentraRuntimeHostedService> logger)
    {
        _registry = registry;
        _serviceProvider = serviceProvider;
        _options = options;
        _registrations = registrations.ToArray();
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogCentraInitialized(_options.Value.AppId);

        foreach (var reg in _registrations)
        {
            var driver = _registry.GetPubSubDriver(reg.PubSubName);
            if (driver is null)
            {
                continue;
            }

            var localReg = reg;
            await driver.SubscribeAsync(
                localReg.PubSubName,
                localReg.Topic,
                async (payload, headers, ct) =>
                {
                    return await DispatchEventAsync(localReg, payload, headers, ct).ConfigureAwait(false);
                },
                localReg.DeadLetterTopic,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var reg in _registrations)
        {
            var driver = _registry.GetPubSubDriver(reg.PubSubName);
            if (driver is not null)
            {
                await driver.UnsubscribeAsync(reg.PubSubName, reg.Topic, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<EventHandlingResult> DispatchEventAsync(
        CentraTopicRegistration reg,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var parentContext = CentraTracePropagator.Extract(headers);
        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartProcessActivity(reg.PubSubName, reg.Topic, parentContext);

        // Ambient context setup
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
            using var scope = _serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetService(reg.HandlerType);
            if (handler is null)
            {
                return EventHandlingResult.Drop;
            }

            // Unpack generic event
            var unpackMethod = typeof(CloudEventUnpacker)
                .GetMethod(nameof(CloudEventUnpacker.Unpack))!
                .MakeGenericMethod(reg.EventType);

            var unpacked = unpackMethod.Invoke(null, [payload, headers]);
            var dataProp = unpacked!.GetType().GetProperty(nameof(UnpackedCloudEvent<object>.Data))!;
            var contextProp = unpacked.GetType().GetProperty(nameof(UnpackedCloudEvent<object>.Context))!;

            var eventData = dataProp.GetValue(unpacked);
            var eventContext = (EventContext)contextProp.GetValue(unpacked)!;

            var handleMethod = reg.HandlerType.GetMethod(nameof(IEventHandler<object>.HandleAsync))!;

            var resilienceProvider = _serviceProvider.GetService<IResiliencePipelineProvider>();
            EventHandlingResult result;
            if (resilienceProvider is not null)
            {
                var pipeline = resilienceProvider.GetPubSubPipeline(reg.PubSubName);
                result = await pipeline.ExecuteAsync(async ct =>
                {
                    var task = (Task<EventHandlingResult>)handleMethod.Invoke(handler, [eventData, eventContext, ct])!;
                    return await task.ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var resultTask = (Task<EventHandlingResult>)handleMethod.Invoke(handler, [eventData, eventContext, cancellationToken])!;
                result = await resultTask.ConfigureAwait(false);
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
