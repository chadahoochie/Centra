namespace Centra.Aspire.Hosting;

/// <summary>
/// Coordinates of the published Centra Control Plane container image.
/// </summary>
/// <remarks>
/// <see cref="Tag"/> tracks the version Centra ships its assemblies and packages under —
/// <c>VersionPrefix</c> in <c>Directory.Build.props</c>, recorded in <c>RELEASE_NOTES.md</c> — so
/// the tag the resource pulls is the tag the release pipeline pushes. A unit test enforces that
/// coupling. Mirror the image into a private registry and point at it with
/// <c>Aspire.Hosting.ContainerResourceBuilderExtensions.WithImageRegistry</c>, or select a
/// different build with <c>WithImageTag</c>.
/// </remarks>
public static class CentraControlPlaneImage
{
    /// <summary>Default container registry hosting the published image.</summary>
    public const string Registry = "ghcr.io";

    /// <summary>Repository path of the published image within <see cref="Registry"/>.</summary>
    public const string Image = "chadahoochie/centra-controlplane";

    /// <summary>Default image tag, matching the Centra framework version.</summary>
    public const string Tag = "1.0.0";

    /// <summary>Port the control plane listens on inside the container.</summary>
    public const int ContainerHttpPort = 8080;
}
