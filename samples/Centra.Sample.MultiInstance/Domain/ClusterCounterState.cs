namespace Centra.Sample.MultiInstance.Domain;

public sealed record ClusterCounterState(
    long Counter,
    string LastUpdatedByInstanceId,
    DateTimeOffset LastUpdatedAtUtc);
