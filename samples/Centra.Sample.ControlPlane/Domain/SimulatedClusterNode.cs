namespace Centra.Sample.ControlPlane.Domain;

public sealed record SimulatedClusterNode(
    string ClusterId,
    string AppId,
    string InstanceId,
    string? Token = null,
    string Status = "Healthy",
    IReadOnlyDictionary<string, string>? Metadata = null);
