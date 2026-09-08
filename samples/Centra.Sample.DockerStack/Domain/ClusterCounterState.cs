namespace Centra.Sample.DockerStack.Domain;

public sealed record ClusterCounterState(
    long Counter,
    string LastUpdatedByInstanceId,
    DateTimeOffset LastUpdatedAtUtc);
