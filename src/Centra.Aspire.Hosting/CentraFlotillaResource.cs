using Aspire.Hosting.ApplicationModel;

namespace Centra.Aspire.Hosting;

/// <summary>
/// The Flotilla Consensus Server, orchestrated as a container from a published image or Dockerfile.
/// </summary>
/// <remarks>
/// Deriving from <see cref="ContainerResource"/> makes the resource launchable as an independent container.
/// Supports TCP, UDP, gRPC, and HTTP endpoints matching Flotilla's multi-protocol transport architecture.
/// </remarks>
public sealed class CentraFlotillaResource : ContainerResource, IResourceWithConnectionString
{
    /// <summary>Name of the TCP endpoint.</summary>
    public const string TcpEndpointName = "tcp";

    /// <summary>Name of the UDP endpoint.</summary>
    public const string UdpEndpointName = "udp";

    /// <summary>Name of the gRPC endpoint.</summary>
    public const string GrpcEndpointName = "grpc";

    /// <summary>Name of the HTTP endpoint.</summary>
    public const string HttpEndpointName = "http";

    /// <summary>Creates the resource under the supplied Aspire resource name.</summary>
    /// <param name="name">Aspire resource name.</param>
    public CentraFlotillaResource(string name) : base(name)
    {
    }

    /// <summary>Reference to the Flotilla TCP endpoint.</summary>
    public EndpointReference TcpEndpoint => new(this, TcpEndpointName);

    /// <summary>Reference to the Flotilla UDP endpoint.</summary>
    public EndpointReference UdpEndpoint => new(this, UdpEndpointName);

    /// <summary>Reference to the Flotilla gRPC endpoint.</summary>
    public EndpointReference GrpcEndpoint => new(this, GrpcEndpointName);

    /// <summary>Reference to the Flotilla HTTP endpoint.</summary>
    public EndpointReference HttpEndpoint => new(this, HttpEndpointName);

    /// <summary>Connection string expression exposing the primary TCP endpoint address.</summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{TcpEndpoint.Property(EndpointProperty.Host)}:{TcpEndpoint.Property(EndpointProperty.Port)}");
}
