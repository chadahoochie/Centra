namespace Centra.ControlPlane.Topology;

public sealed record ClientNodeInfo(
    string AppId,
    string InstanceId,
    string Status,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset LastHeartbeatUtc,
    IReadOnlyDictionary<string, string>? Metadata);
