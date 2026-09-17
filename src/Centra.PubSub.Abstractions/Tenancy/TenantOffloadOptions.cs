namespace Centra.PubSub.Tenancy;

public sealed class TenantOffloadOptions
{
    public double TrafficShareThreshold { get; set; } = 0.30;

    public double DurationMultiplierThreshold { get; set; } = 3.0;

    public double? DurationAbsoluteThresholdMs { get; set; }

    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromSeconds(60);

    public int MinSampleCount { get; set; } = 20;

    public TimeSpan CooldownPeriod { get; set; } = TimeSpan.FromSeconds(30);

    public TenantOffloadStrategyType OffloadStrategy { get; set; } = TenantOffloadStrategyType.InProcessFairScheduler;

    public int MaxConcurrencyPerTenant { get; set; } = 2;

    public int PerTenantQueueCapacity { get; set; } = 500;

    public int OffloadShardCount { get; set; } = 4;

    public TimeSpan LaneIdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan OffloadTopicIdleTtl { get; set; } = TimeSpan.FromMinutes(5);

    public bool EnablePublisherBypassing { get; set; } = true;

    public string OffloadTopicPattern { get; set; } = "{topic}.offload.{tenantId}";

    public TimeSpan ReapQuarantineWindow { get; set; } = TimeSpan.FromSeconds(2);

    public bool EnableDistributedReaperLock { get; set; } = true;
}
