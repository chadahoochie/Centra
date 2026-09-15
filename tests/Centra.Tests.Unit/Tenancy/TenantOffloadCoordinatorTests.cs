using Centra.PubSub;
using Centra.PubSub.Tenancy;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Centra.Tests.Unit.Tenancy;

public sealed class TenantOffloadCoordinatorTests
{
    [Fact]
    public void IsTenantOffloaded_TransitionsToOffloadedWhenNoisy()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions
        {
            WindowDuration = TimeSpan.FromSeconds(60),
            MinSampleCount = 5,
            DurationMultiplierThreshold = 3.0
        };

        var tracker = new RollingWindowTenantMetricsTracker(options, fakeTime);
        var publisher = new TestPubSubPublisher();
        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, null, options);
        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options);

        // Record slow executions
        for (var i = 0; i < 5; i++)
        {
            coordinator.RecordExecution("tenant-slow", "orders", 500.0);
        }
        for (var i = 0; i < 20; i++)
        {
            coordinator.RecordExecution("tenant-fast", "orders", 10.0);
        }

        var isOffloaded = coordinator.IsTenantOffloaded("tenant-slow", "orders", out var reason);

        Assert.True(isOffloaded);
        Assert.True(reason.HasFlag(TenantOffloadReason.DisproportionateOperationDuration));
    }

    [Fact]
    public void Cooldown_RestoresTenantToNormalAfterCooldownPeriod()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions
        {
            WindowDuration = TimeSpan.FromSeconds(60),
            CooldownPeriod = TimeSpan.FromSeconds(30),
            MinSampleCount = 5,
            DurationMultiplierThreshold = 3.0
        };

        var tracker = new RollingWindowTenantMetricsTracker(options, fakeTime);
        var publisher = new TestPubSubPublisher();
        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, null, options);
        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options, fakeTime);

        // Force offload
        coordinator.ForceOffload("tenant-test", "orders", TenantOffloadReason.HighTrafficShare);

        Assert.True(coordinator.IsTenantOffloaded("tenant-test", "orders", out _));

        // Advance time past cooldown period
        fakeTime.Advance(TimeSpan.FromSeconds(35));

        // Since tracker has 0 messages, ShouldOffload is false. Cooldown period elapsed.
        var isOffloadedAfterCooldown = coordinator.IsTenantOffloaded("tenant-test", "orders", out var reason);

        Assert.False(isOffloadedAfterCooldown);
        Assert.Equal(TenantOffloadReason.None, reason);
    }

    [Fact]
    public void ForceOffload_And_ClearOffload_WorkCorrectly()
    {
        var options = new TenantOffloadOptions();
        var tracker = new RollingWindowTenantMetricsTracker(options);
        var publisher = new TestPubSubPublisher();
        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, null, options);
        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options);

        coordinator.ForceOffload("tenant-pinned", "orders", TenantOffloadReason.Manual);
        Assert.True(coordinator.IsTenantOffloaded("tenant-pinned", "orders", out var reason));
        Assert.True(reason.HasFlag(TenantOffloadReason.Manual));

        coordinator.ClearOffload("tenant-pinned", "orders");
        Assert.False(coordinator.IsTenantOffloaded("tenant-pinned", "orders", out _));
    }

    [Fact]
    public void ResolvePublishTopic_BypassesToOffloadTopicWhenOffloaded()
    {
        var options = new TenantOffloadOptions
        {
            OffloadStrategy = TenantOffloadStrategyType.EphemeralBrokerTopic,
            OffloadTopicPattern = "{topic}.offload.{tenantId}",
            EnablePublisherBypassing = true
        };

        var tracker = new RollingWindowTenantMetricsTracker(options);
        var publisher = new TestPubSubPublisher();
        var strategy = new EphemeralBrokerTopicOffloadStrategy(publisher, null, options);
        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options);

        coordinator.ForceOffload("tenant-X", "orders", TenantOffloadReason.HighTrafficShare);

        var offloadTopic = coordinator.ResolvePublishTopic("pubsub", "orders", "tenant-X");
        var normalTopic = coordinator.ResolvePublishTopic("pubsub", "orders", "tenant-normal");

        Assert.Equal("orders.offload.tenant-X", offloadTopic);
        Assert.Equal("orders", normalTopic);
    }
}
