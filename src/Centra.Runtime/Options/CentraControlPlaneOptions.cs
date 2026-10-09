namespace Centra;

public sealed class CentraControlPlaneOptions
{
    public string? Endpoint { get; set; }
    public IReadOnlyList<string> Endpoints { get; set; } = [];
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
    public bool EnableLiveSync { get; set; } = true;
    public string ClusterId { get; set; } = "default";
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public string? ClusterToken { get; set; }
    public bool UseHmacAuthentication { get; set; } = false;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
