namespace Centra.PubSub.Tenancy;

public readonly record struct TenantTrafficStats(
    string TenantId,
    long MessageCount,
    double TrafficShareRatio,
    double AverageDurationMs,
    double DurationRatio,
    TenantOffloadState State,
    TenantOffloadReason Reason);
