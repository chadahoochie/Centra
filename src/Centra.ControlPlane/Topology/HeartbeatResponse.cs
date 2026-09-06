namespace Centra.ControlPlane.Topology;

public readonly record struct HeartbeatResponse(
    bool Acknowledged,
    DateTimeOffset ServerTimeUtc);
