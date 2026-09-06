using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Centra.Components;

namespace Centra.Aspire.Hosting;

public static class CentraAspireExtensions
{
    public static IResourceBuilder<CentraControlPlaneResource> AddCentraControlPlane(
        this IDistributedApplicationBuilder builder,
        string name = "centra-controlplane",
        int? port = null)
    {
        var resource = new CentraControlPlaneResource(name);

        return builder.AddResource(resource)
            .WithHttpEndpoint(port: port, name: CentraControlPlaneResource.HttpEndpointName);
    }

    public static IResourceBuilder<T> WithCentra<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<CentraControlPlaneResource> controlPlane)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(controlPlane);

        return builder
            .WithEnvironment("Centra__ControlPlaneEndpoint", controlPlane.Resource.HttpEndpoint)
            .WithEnvironment("Centra__AppId", builder.Resource.Name);
    }
}
