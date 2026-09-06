namespace Centra.Sample.MultiInstance.Domain;

public sealed record ClusterNodeInfo(
    string InstanceId,
    string AppId,
    bool IsLeader,
    int ProcessedTasksCount,
    DateTimeOffset StartedAtUtc,
    string? HostAddress);
