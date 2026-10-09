namespace Centra.ControlPlane.Security;

public sealed class ControlPlaneSecurityOptions
{
    public bool Enabled { get; set; } = false;
    public bool RequireHmacSignature { get; set; } = false;
    public bool RequireRemoteIpMatch { get; set; } = false;
    public bool EnableReverseHealthProbe { get; set; } = false;
    public bool RedactWorkflowData { get; set; } = true;
    public TimeSpan AllowedClockDrift { get; set; } = TimeSpan.FromSeconds(30);
    public Dictionary<string, string> ClusterTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? AdminToken { get; set; }
}
