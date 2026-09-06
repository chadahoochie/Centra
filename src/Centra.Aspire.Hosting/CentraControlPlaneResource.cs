using Aspire.Hosting.ApplicationModel;

namespace Centra.Aspire.Hosting;

public sealed class CentraControlPlaneResource : Resource, IResourceWithEndpoints
{
    public const string HttpEndpointName = "http";

    public CentraControlPlaneResource(string name) : base(name)
    {
    }

    public EndpointReference HttpEndpoint => new(this, HttpEndpointName);
}
