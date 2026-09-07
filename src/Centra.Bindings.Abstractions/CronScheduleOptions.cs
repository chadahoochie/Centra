namespace Centra.Bindings;

public sealed record CronScheduleOptions(
    TimeZoneInfo? TimeZone = null,
    CronMissedRunBehavior MissedRunBehavior = CronMissedRunBehavior.Skip,
    bool UseDistributedCoordination = true,
    TimeSpan? LockTimeout = null);
