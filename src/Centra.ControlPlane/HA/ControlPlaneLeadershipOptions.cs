namespace Centra.ControlPlane.HA;

public sealed class ControlPlaneLeadershipOptions
{
    public bool Enabled { get; set; } = false;
    public string PublicEndpoint { get; set; } = "http://localhost:8080";
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(4);
    public TimeSpan RenewInterval { get; set; } = TimeSpan.FromMilliseconds(1200);
    public string LockStoreName { get; set; } = "lockstore";
    public string LockResource { get; set; } = "centra:controlplane:leader";
}
