namespace Centra.ControlPlane.Dashboard;

public sealed class ControlPlaneDashboardOptions
{
    public bool Enabled { get; set; } = true;
    public string Path { get; set; } = "/dashboard";
    public string Title { get; set; } = "Centra Control Plane Dashboard";
}
