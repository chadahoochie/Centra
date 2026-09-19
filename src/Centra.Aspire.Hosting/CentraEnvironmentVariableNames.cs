namespace Centra.Aspire.Hosting;

/// <summary>
/// Environment variable names Centra's hosting runtime binds its configuration from.
/// </summary>
public static class CentraEnvironmentVariableNames
{
    /// <summary>Absolute HTTP address of the Centra Control Plane.</summary>
    public const string ControlPlaneEndpoint = "Centra__ControlPlaneEndpoint";

    /// <summary>Logical application identifier reported to the Control Plane.</summary>
    public const string AppId = "Centra__AppId";
}
