namespace Centra.ControlPlane.Diagnostics;

public sealed record ControlPlaneHealthResponse(
    string Status,
    string Service,
    string Version,
    DateTimeOffset TimestampUtc,
    string Role = "Active",
    bool IsLeader = true,
    string? LeaderEndpoint = null);
