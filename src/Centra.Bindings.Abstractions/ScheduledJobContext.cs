namespace Centra.Bindings;

public readonly record struct ScheduledJobContext(
    string JobName,
    DateTimeOffset ScheduledTime,
    DateTimeOffset ActualTime,
    long Iteration,
    CancellationToken CancellationToken);
