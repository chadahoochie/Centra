using Aspire.Hosting.ApplicationModel;

namespace Centra.Aspire.Hosting;

/// <summary>
/// The Centra Control Plane, orchestrated as a container from a published image.
/// </summary>
/// <remarks>
/// Deriving from <see cref="ContainerResource"/> is what makes the resource launchable: the
/// orchestrator starts it from the <see cref="ContainerImageAnnotation"/> applied by
/// <c>AddCentraControlPlane</c>. A bare <see cref="Resource"/> would still publish an endpoint
/// address, but for a process that never exists.
/// </remarks>
public sealed class CentraControlPlaneResource : ContainerResource
{
    /// <summary>Name of the HTTP endpoint the Control Plane serves its API on.</summary>
    public const string HttpEndpointName = "http";

    /// <summary>Creates the resource under the supplied Aspire resource name.</summary>
    /// <param name="name">Aspire resource name.</param>
    public CentraControlPlaneResource(string name) : base(name)
    {
    }

    /// <summary>Reference to the Control Plane's HTTP endpoint.</summary>
    public EndpointReference HttpEndpoint => new(this, HttpEndpointName);
}
