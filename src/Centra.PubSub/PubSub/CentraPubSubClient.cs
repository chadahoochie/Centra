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

    public CentraPubSubClient(
        ComponentRegistry registry,
        string appId,
        string defaultPubSubName = "pubsub",
        IResiliencePipelineProvider? resilienceProvider = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _appId = !string.IsNullOrWhiteSpace(appId) ? appId : "centra-app";
        _defaultPubSubName = !string.IsNullOrWhiteSpace(defaultPubSubName) ? defaultPubSubName : "pubsub";
        _resilienceProvider = resilienceProvider;
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

        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartPublishActivity(pubSubName, topic);

        var mode = options?.Mode ?? CloudEventMode.Binary;
        var packed = CloudEventPacker.Pack(data, _appId, mode, subject: null, additionalMetadata: options?.Metadata);

        try
        {
            if (_resilienceProvider is not null && options?.DisableResilience != true)
            {
                var pipeline = _resilienceProvider.GetPubSubPipeline(pubSubName);
                await pipeline.ExecuteAsync(async ct => await driver.PublishAsync(pubSubName, topic, packed.Payload, packed.Headers, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await driver.PublishAsync(pubSubName, topic, packed.Payload, packed.Headers, cancellationToken).ConfigureAwait(false);
            }

            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordPubSubPublished(pubSubName, topic, "success", durationMs);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordPubSubPublished(pubSubName, topic, "error", durationMs);
            throw;
        }
    }
}
