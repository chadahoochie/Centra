using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Events;
using Centra.PubSub;
using Centra.Registry;
using Centra.Resilience;

namespace Centra.PubSub;

public sealed class CentraPubSubClient : IPubSubClient
{
    private readonly ComponentRegistry _registry;
    private readonly string _appId;
    private readonly string _defaultPubSubName;
    private readonly IResiliencePipelineProvider? _resilienceProvider;
    private readonly Tenancy.ITenantOffloadCoordinator? _offloadCoordinator;

    public CentraPubSubClient(
        ComponentRegistry registry,
        string appId,
        string defaultPubSubName = "pubsub",
        IResiliencePipelineProvider? resilienceProvider = null)
        : this(registry, appId, defaultPubSubName, resilienceProvider, offloadCoordinator: null)
    {
    }

    public CentraPubSubClient(
        ComponentRegistry registry,
        string appId,
        string defaultPubSubName,
        IResiliencePipelineProvider? resilienceProvider,
        Tenancy.ITenantOffloadCoordinator? offloadCoordinator)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _appId = !string.IsNullOrWhiteSpace(appId) ? appId : "centra-app";
        _defaultPubSubName = !string.IsNullOrWhiteSpace(defaultPubSubName) ? defaultPubSubName : "pubsub";
        _resilienceProvider = resilienceProvider;
        _offloadCoordinator = offloadCoordinator;
    }

    public ValueTask PublishAsync<T>(
        string topic,
        T data,
        PubSubPublishOptions? options = null,
        CancellationToken cancellationToken = default) =>
        PublishAsync(_defaultPubSubName, topic, data, options, cancellationToken);

    public async ValueTask PublishAsync<T>(
        string pubSubName,
        string topic,
        T data,
        PubSubPublishOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var driver = _registry.GetPubSubDriver(pubSubName);
        if (driver is null)
        {
            throw new InvalidOperationException($"No PubSub driver registered for pubsub '{pubSubName}'");
        }

        var tenantId = CentraAmbientContext.TenantId;
        if (options?.Metadata != null && options.Metadata.TryGetValue(CloudEventConstants.TenantIdHeader, out var customTenantId))
        {
            tenantId = customTenantId;
        }

        var targetTopic = _offloadCoordinator?.ResolvePublishTopic(pubSubName, topic, tenantId) ?? topic;

        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartPublishActivity(pubSubName, targetTopic);

        var mode = options?.Mode ?? CloudEventMode.Binary;
        var packed = CloudEventPacker.Pack(data, _appId, mode, subject: null, additionalMetadata: options?.Metadata);

        try
        {
            if (_resilienceProvider is not null && options?.DisableResilience != true)
            {
                var pipeline = _resilienceProvider.GetPubSubPipeline(pubSubName);
                await pipeline.ExecuteAsync(async ct => await driver.PublishAsync(pubSubName, targetTopic, packed.Payload, packed.Headers, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await driver.PublishAsync(pubSubName, targetTopic, packed.Payload, packed.Headers, cancellationToken).ConfigureAwait(false);
            }

            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordPubSubPublished(pubSubName, targetTopic, "success", durationMs);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordPubSubPublished(pubSubName, targetTopic, "error", durationMs);
            throw;
        }
    }
}
