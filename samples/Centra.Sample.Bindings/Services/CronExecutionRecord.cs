namespace Centra.Sample.Bindings.Services;

/// <summary>
/// Record representing a successful execution of a cron tick by a specific cluster node.
/// </summary>
public sealed record CronExecutionRecord(
    string NodeId,
    long Iteration,
    DateTimeOffset ScheduledTime,
    DateTimeOffset ExecutedTime);
