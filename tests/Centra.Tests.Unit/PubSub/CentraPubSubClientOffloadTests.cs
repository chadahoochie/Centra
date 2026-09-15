using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Events;
using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Centra.Registry;
using Centra.Tests.Unit.Tenancy;
using NSubstitute;
using Xunit;

namespace Centra.Tests.Unit.PubSub;

public sealed class CentraPubSubClientOffloadTests
{
    private readonly ComponentRegistry _registry = new();
    private readonly IPubSubDriver _driver = Substitute.For<IPubSubDriver>();
    private readonly TenantOffloadOptions _options;
    private readonly RollingWindowTenantMetricsTracker _tracker;
    private readonly EphemeralBrokerTopicOffloadStrategy _strategy;
    private readonly TenantOffloadCoordinator _coordinator;

    public CentraPubSubClientOffloadTests()
    {
        _registry.RegisterPubSubDriver("pubsub", _driver);
        _options = new TenantOffloadOptions
        {
            EnablePublisherBypassing = true,
            OffloadTopicPattern = "{topic}.offload.{tenantId}"
        };
        _tracker = new RollingWindowTenantMetricsTracker(_options);
        var testPublisher = new TestPubSubPublisher();
        _strategy = new EphemeralBrokerTopicOffloadStrategy(testPublisher, null, _options);
        _coordinator = new TenantOffloadCoordinator(_tracker, _strategy, _options);
    }

    [Fact]
    public async Task PublishAsync_WhenTenantIsOffloaded_PublishesToOffloadTopic()
    {
        _coordinator.ForceOffload("tenant-burst", "orders", TenantOffloadReason.HighTrafficShare);

        var client = new CentraPubSubClient(_registry, "orders-app", "pubsub", resilienceProvider: null, _coordinator);

        CentraAmbientContext.TenantId = "tenant-burst";
        try
        {
            await client.PublishAsync("orders", new { Id = 123 });

            await _driver.Received(1).PublishAsync(
                "pubsub",
                "orders.offload.tenant-burst",
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            CentraAmbientContext.Clear();
        }
    }

    [Fact]
    public async Task PublishAsync_WithCustomTenantInMetadata_UsesCustomTenantForOffloadResolution()
    {
        _coordinator.ForceOffload("tenant-custom", "orders", TenantOffloadReason.HighTrafficShare);

        var client = new CentraPubSubClient(_registry, "orders-app", "pubsub", resilienceProvider: null, _coordinator);

        var publishOptions = new PubSubPublishOptions
        {
            Metadata = new Dictionary<string, string>
            {
                [CloudEventConstants.TenantIdHeader] = "tenant-custom"
            }
        };

        await client.PublishAsync("orders", new { Id = 456 }, publishOptions);

        await _driver.Received(1).PublishAsync(
            "pubsub",
            "orders.offload.tenant-custom",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenTenantNotOffloaded_PublishesToBaseTopic()
    {
        var client = new CentraPubSubClient(_registry, "orders-app", "pubsub", resilienceProvider: null, _coordinator);

        CentraAmbientContext.TenantId = "tenant-normal";
        try
        {
            await client.PublishAsync("orders", new { Id = 789 });

            await _driver.Received(1).PublishAsync(
                "pubsub",
                "orders",
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            CentraAmbientContext.Clear();
        }
    }
}
