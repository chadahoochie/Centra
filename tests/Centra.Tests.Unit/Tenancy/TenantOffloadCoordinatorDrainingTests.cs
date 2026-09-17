using Centra.PubSub.Tenancy;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Centra.Tests.Unit.Tenancy;

public sealed class TenantOffloadCoordinatorDrainingTests
{
    [Fact]
    public void Cooldown_Transitions_To_Draining_And_Cuts_Off_Publisher()
    {
        var tracker = Substitute.For<ITenantMetricsTracker>();
        var strategy = Substitute.For<ITenantOffloadStrategy>();
        strategy.ResolvePublishTopic("tenant.orders", "tenant-mega").Returns("tenant.orders.offload.tenant-mega");

        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions
        {
            EnablePublisherBypassing = true,
            CooldownPeriod = TimeSpan.FromSeconds(10)
        };

        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options, fakeTime);

        // 1. Force offload
        coordinator.ForceOffload("tenant-mega", "tenant.orders", TenantOffloadReason.HighTrafficShare);
        Assert.Equal(TenantOffloadState.Offloaded, coordinator.GetTenantState("tenant-mega", "tenant.orders"));
        Assert.Equal("tenant.orders.offload.tenant-mega", coordinator.ResolvePublishTopic("pubsub", "tenant.orders", "tenant-mega"));

        // 2. Anomaly ceases (tracker reports ShouldOffload = false)
        tracker.ShouldOffload("tenant-mega", "tenant.orders", out Arg.Any<TenantOffloadReason>()).Returns(false);

        // Calling IsTenantOffloaded should detect anomaly subsided and transition to Draining
        var isOffloaded = coordinator.IsTenantOffloaded("tenant-mega", "tenant.orders", out _);
        Assert.True(isOffloaded); // Still in offload handling lifecycle
        Assert.Equal(TenantOffloadState.Draining, coordinator.GetTenantState("tenant-mega", "tenant.orders"));

        // During Draining, publishers are CUT OFF from offload topic; routed to base topic
        Assert.Equal("tenant.orders", coordinator.ResolvePublishTopic("pubsub", "tenant.orders", "tenant-mega"));

        // 3. Advance past cooldown period
        fakeTime.Advance(TimeSpan.FromSeconds(11));

        var postCooldownOffloaded = coordinator.IsTenantOffloaded("tenant-mega", "tenant.orders", out _);
        Assert.False(postCooldownOffloaded);
        Assert.Equal(TenantOffloadState.Normal, coordinator.GetTenantState("tenant-mega", "tenant.orders"));
        Assert.Equal("tenant.orders", coordinator.ResolvePublishTopic("pubsub", "tenant.orders", "tenant-mega"));
    }

    [Fact]
    public void Draining_ReTriggers_To_Offloaded_If_Surge_Resumes()
    {
        var tracker = Substitute.For<ITenantMetricsTracker>();
        var strategy = Substitute.For<ITenantOffloadStrategy>();
        strategy.ResolvePublishTopic("tenant.orders", "tenant-mega").Returns("tenant.orders.offload.tenant-mega");

        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions
        {
            EnablePublisherBypassing = true,
            CooldownPeriod = TimeSpan.FromSeconds(10)
        };

        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options, fakeTime);

        // Force offload, then anomaly subsides -> transitions to Draining
        coordinator.ForceOffload("tenant-mega", "tenant.orders", TenantOffloadReason.HighTrafficShare);
        tracker.ShouldOffload("tenant-mega", "tenant.orders", out Arg.Any<TenantOffloadReason>()).Returns(false);
        Assert.True(coordinator.IsTenantOffloaded("tenant-mega", "tenant.orders", out _));
        Assert.Equal(TenantOffloadState.Draining, coordinator.GetTenantState("tenant-mega", "tenant.orders"));

        // Second burst resumes before cooldown finishes: tracker reports ShouldOffload = true again
        tracker.ShouldOffload("tenant-mega", "tenant.orders", out Arg.Any<TenantOffloadReason>())
            .Returns(x =>
            {
                x[2] = TenantOffloadReason.DisproportionateOperationDuration;
                return true;
            });

        var reTriggered = coordinator.IsTenantOffloaded("tenant-mega", "tenant.orders", out var reason);
        Assert.True(reTriggered);
        Assert.Equal(TenantOffloadReason.DisproportionateOperationDuration, reason);
        Assert.Equal(TenantOffloadState.Offloaded, coordinator.GetTenantState("tenant-mega", "tenant.orders"));
        Assert.Equal("tenant.orders.offload.tenant-mega", coordinator.ResolvePublishTopic("pubsub", "tenant.orders", "tenant-mega"));
    }

    [Fact]
    public void ClearOffload_Resets_State_To_Normal()
    {
        var tracker = Substitute.For<ITenantMetricsTracker>();
        var strategy = Substitute.For<ITenantOffloadStrategy>();
        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions();

        var coordinator = new TenantOffloadCoordinator(tracker, strategy, options, fakeTime);

        coordinator.ForceOffload("tenant-1", "topic-1", TenantOffloadReason.HighTrafficShare);
        Assert.Equal(TenantOffloadState.Offloaded, coordinator.GetTenantState("tenant-1", "topic-1"));

        coordinator.ClearOffload("tenant-1", "topic-1");
        Assert.Equal(TenantOffloadState.Normal, coordinator.GetTenantState("tenant-1", "topic-1"));
    }

    [Theory]
    [InlineData(null, "topic")]
    [InlineData("", "topic")]
    [InlineData("tenant", null)]
    [InlineData("tenant", "")]
    [InlineData("unknown", "topic")]
    public void GetTenantState_Returns_Normal_For_Empty_Or_Unknown_Inputs(string? tenantId, string? topic)
    {
        var tracker = Substitute.For<ITenantMetricsTracker>();
        var strategy = Substitute.For<ITenantOffloadStrategy>();
        var coordinator = new TenantOffloadCoordinator(tracker, strategy);

        Assert.Equal(TenantOffloadState.Normal, coordinator.GetTenantState(tenantId!, topic!));
    }
}
