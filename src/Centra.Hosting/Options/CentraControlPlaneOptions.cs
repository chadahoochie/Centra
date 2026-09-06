namespace Centra.Hosting.Options;

public sealed class CentraControlPlaneOptions
{
    public string? Endpoint { get; set; }
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);
    public bool EnableLiveSync { get; set; } = true;
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
}
