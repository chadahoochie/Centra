namespace Centra.ControlPlane.Topology;

public readonly record struct HeartbeatRequest(
    string AppId,
    string InstanceId,
    string Status,
    IReadOnlyDictionary<string, string>? Metadata);
