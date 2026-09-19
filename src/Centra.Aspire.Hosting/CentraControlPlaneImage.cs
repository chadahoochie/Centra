namespace Centra.Aspire.Hosting;

/// <summary>
/// Coordinates of the published Centra Control Plane container image.
/// </summary>
/// <remarks>
/// <see cref="Tag"/> is a floating minor-version tag republished by the release pipeline
/// alongside the exact build version. Pin an exact tag with
/// <c>Aspire.Hosting.ContainerResourceBuilderExtensions.WithImageTag</c>, or mirror the image
/// into a private registry and point at it with
/// <c>Aspire.Hosting.ContainerResourceBuilderExtensions.WithImageRegistry</c>.
/// </remarks>
public static class CentraControlPlaneImage
{
    /// <summary>Default container registry hosting the published image.</summary>
    public const string Registry = "ghcr.io";

    /// <summary>Repository path of the published image within <see cref="Registry"/>.</summary>
    public const string Image = "chadahoochie/centra-controlplane";

    /// <summary>Default image tag.</summary>
    public const string Tag = "1.0";

    /// <summary>Port the control plane listens on inside the container.</summary>
    public const int ContainerHttpPort = 8080;
}
