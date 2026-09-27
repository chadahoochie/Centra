namespace Centra.ControlPlane.Diagnostics;

public sealed record ControlPlaneHealthResponse(
    string Status,
    string Service,
    string Version,
    DateTimeOffset TimestampUtc);
