using Centra.PubSub.Tenancy;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Centra.Tests.Unit.Tenancy;

public sealed class RollingWindowTenantMetricsTrackerTests
{
    [Fact]
    public void RecordExecution_TracksMessageCountsAndDurations()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions
        {
            WindowDuration = TimeSpan.FromSeconds(60),
            MinSampleCount = 5
        };

        var tracker = new RollingWindowTenantMetricsTracker(options, fakeTime);

        for (var i = 0; i < 10; i++)
        {
            tracker.RecordExecution("tenant-1", "orders", 100.0);
        }

        var stats = tracker.GetTenantStats("tenant-1", "orders");
        Assert.Equal("tenant-1", stats.TenantId);
        Assert.Equal(10, stats.MessageCount);
        Assert.Equal(100.0, stats.AverageDurationMs, precision: 1);
        Assert.Equal(1.0, stats.TrafficShareRatio, precision: 2);
    }

    [Fact]
    public void TrafficShare_ExceedingThreshold_TriggersHighTrafficShare()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions
        {
            WindowDuration = TimeSpan.FromSeconds(60),
            MinSampleCount = 10,
            TrafficShareThreshold = 0.50
        };

        var tracker = new RollingWindowTenantMetricsTracker(options, fakeTime);

        // Tenant 1 sends 15 messages (75% of total)
        for (var i = 0; i < 15; i++)
        {
            tracker.RecordExecution("tenant-1", "orders", 10.0);
        }

        // Tenant 2 sends 5 messages (25% of total)
        for (var i = 0; i < 5; i++)
        {
            tracker.RecordExecution("tenant-2", "orders", 10.0);
        }

        var shouldOffloadT1 = tracker.ShouldOffload("tenant-1", "orders", out var reasonT1);
        var shouldOffloadT2 = tracker.ShouldOffload("tenant-2", "orders", out var reasonT2);

        Assert.True(shouldOffloadT1);
        Assert.True(reasonT1.HasFlag(TenantOffloadReason.HighTrafficShare));

        Assert.False(shouldOffloadT2);
        Assert.Equal(TenantOffloadReason.None, reasonT2);
    }

    [Fact]
    public void DurationRatio_ExceedingThreshold_TriggersDisproportionateOperationDuration()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions
        {
            WindowDuration = TimeSpan.FromSeconds(60),
            MinSampleCount = 5,
            DurationMultiplierThreshold = 3.0
        };

        var tracker = new RollingWindowTenantMetricsTracker(options, fakeTime);

        // Tenant 1 takes 300ms
        for (var i = 0; i < 5; i++)
        {
            tracker.RecordExecution("tenant-slow", "orders", 300.0);
        }

        // Normal tenants take 10ms
        for (var i = 0; i < 20; i++)
        {
            tracker.RecordExecution("tenant-fast", "orders", 10.0);
        }

        var shouldOffload = tracker.ShouldOffload("tenant-slow", "orders", out var reason);

        Assert.True(shouldOffload);
        Assert.True(reason.HasFlag(TenantOffloadReason.DisproportionateOperationDuration));
    }

    [Fact]
    public void AbsoluteDuration_ExceedingThreshold_TriggersDisproportionateOperationDuration()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions
        {
            WindowDuration = TimeSpan.FromSeconds(60),
            MinSampleCount = 5,
            DurationMultiplierThreshold = 100.0, // Set high so relative ratio won't trigger
            DurationAbsoluteThresholdMs = 500.0
        };

        var tracker = new RollingWindowTenantMetricsTracker(options, fakeTime);

        for (var i = 0; i < 5; i++)
        {
            tracker.RecordExecution("tenant-abs-slow", "orders", 600.0);
        }

        var shouldOffload = tracker.ShouldOffload("tenant-abs-slow", "orders", out var reason);

        Assert.True(shouldOffload);
        Assert.True(reason.HasFlag(TenantOffloadReason.DisproportionateOperationDuration));
    }

    [Fact]
    public void MinSampleCount_GuardsAgainstPrematureTrigger()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions
        {
            WindowDuration = TimeSpan.FromSeconds(60),
            MinSampleCount = 10,
            TrafficShareThreshold = 0.30
        };

        var tracker = new RollingWindowTenantMetricsTracker(options, fakeTime);

        // Only 3 messages (100% share, but below MinSampleCount of 10)
        for (var i = 0; i < 3; i++)
        {
            tracker.RecordExecution("tenant-burst", "orders", 10.0);
        }

        var shouldOffload = tracker.ShouldOffload("tenant-burst", "orders", out var reason);

        Assert.False(shouldOffload);
        Assert.Equal(TenantOffloadReason.None, reason);
    }

    [Fact]
    public void Reset_ClearsWindowData()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new TenantOffloadOptions();
        var tracker = new RollingWindowTenantMetricsTracker(options, fakeTime);

        tracker.RecordExecution("tenant-1", "orders", 10.0);
        tracker.RecordExecution("tenant-2", "orders", 10.0);

        tracker.Reset("tenant-1");

        var stats1 = tracker.GetTenantStats("tenant-1", "orders");
        var stats2 = tracker.GetTenantStats("tenant-2", "orders");

        Assert.Equal(0, stats1.MessageCount);
        Assert.Equal(1, stats2.MessageCount);

        tracker.Reset();
        var stats2AfterAll = tracker.GetTenantStats("tenant-2", "orders");
        Assert.Equal(0, stats2AfterAll.MessageCount);
    }
}
