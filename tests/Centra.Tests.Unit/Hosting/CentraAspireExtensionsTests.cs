using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Centra.Aspire.Hosting;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraAspireExtensionsTests
{
    [Fact]
    public void AddCentraControlPlane_NullBuilder_ThrowsArgumentNullException()
    {
        IDistributedApplicationBuilder builder = null!;
        Should.Throw<ArgumentNullException>(() => builder.AddCentraControlPlane());
    }

    [Fact]
    public void AddCentraControlPlane_RegistersResourceAndEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder();

        var resourceBuilder = builder.AddCentraControlPlane("my-controlplane", 8080);

        resourceBuilder.ShouldNotBeNull();
        resourceBuilder.Resource.Name.ShouldBe("my-controlplane");
        resourceBuilder.Resource.HttpEndpoint.EndpointName.ShouldBe(CentraControlPlaneResource.HttpEndpointName);
    }

    [Fact]
    public void WithCentra_ControlPlaneResource_SetsEnvironmentVariables()
    {
        var builder = DistributedApplication.CreateBuilder();
        var controlPlane = builder.AddCentraControlPlane("test-cp");
        var container = builder.AddContainer("my-app", "alpine");

        container.WithCentra(controlPlane);

        container.Resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var annotations).ShouldBeTrue();
        annotations.ShouldNotBeEmpty();
    }

    [Fact]
    public void WithCentra_NullArguments_ThrowsArgumentNullException()
    {
        var builder = DistributedApplication.CreateBuilder();
        var controlPlane = builder.AddCentraControlPlane("test-cp");
        var container = builder.AddContainer("my-app", "alpine");

        IResourceBuilder<ContainerResource> nullBuilder = null!;
        Should.Throw<ArgumentNullException>(() => nullBuilder.WithCentra(controlPlane));
        Should.Throw<ArgumentNullException>(() => container.WithCentra((IResourceBuilder<CentraControlPlaneResource>)null!));
        Should.Throw<ArgumentNullException>(() => container.WithCentra((IResourceBuilder<IResourceWithEndpoints>)null!));
    }

    [Fact]
    public void ProviderExtensions_NullArguments_ThrowArgumentNullException()
    {
        var builder = DistributedApplication.CreateBuilder();
        var container = builder.AddContainer("my-app", "alpine");
        IResourceBuilder<ContainerResource> nullBuilder = null!;

        Should.Throw<ArgumentNullException>(() => nullBuilder.WithCentraRedis(null!));
        Should.Throw<ArgumentNullException>(() => container.WithCentraRedis(null!));

        Should.Throw<ArgumentNullException>(() => nullBuilder.WithCentraRabbitMQ(null!));
        Should.Throw<ArgumentNullException>(() => container.WithCentraRabbitMQ(null!));

        Should.Throw<ArgumentNullException>(() => nullBuilder.WithCentraPostgreSql(null!));
        Should.Throw<ArgumentNullException>(() => container.WithCentraPostgreSql(null!));

        Should.Throw<ArgumentNullException>(() => nullBuilder.WithCentraSqlServer(null!));
        Should.Throw<ArgumentNullException>(() => container.WithCentraSqlServer(null!));
    }
}
