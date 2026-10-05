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

        Should.Throw<ArgumentNullException>(() => nullBuilder.WithCentraFlotilla(null!));
        Should.Throw<ArgumentNullException>(() => container.WithCentraFlotilla(null!));
    }

    [Fact]
    public void WithCentraFlotilla_DefaultEndpoint_SetsEnvironmentVariables()
    {
        var builder = DistributedApplication.CreateBuilder();
        var flotillaServer = builder.AddContainer("flotilla", "my-flotilla").WithEndpoint(name: "tcp", port: 9300);
        var container = builder.AddContainer("my-app", "alpine");

        container.WithCentraFlotilla(flotillaServer);

        container.Resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var annotations).ShouldBeTrue();
        annotations.ShouldNotBeEmpty();
    }

    [Fact]
    public void WithCentraFlotilla_CustomEndpoint_SetsEnvironmentVariables()
    {
        var builder = DistributedApplication.CreateBuilder();
        var flotillaServer = builder.AddContainer("flotilla", "my-flotilla").WithEndpoint(name: "udp", port: 9301);
        var container = builder.AddContainer("my-app", "alpine");

        container.WithCentraFlotilla(flotillaServer, endpointName: "udp");

        container.Resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var annotations).ShouldBeTrue();
        annotations.ShouldNotBeEmpty();
    }

    [Fact]
    public void AddCentraFlotilla_NullBuilder_ThrowsArgumentNullException()
    {
        IDistributedApplicationBuilder builder = null!;
        Should.Throw<ArgumentNullException>(() => builder.AddCentraFlotilla());
    }

    [Fact]
    public void AddCentraFlotilla_RegistersResourceAndEndpoints()
    {
        var builder = DistributedApplication.CreateBuilder();

        var flotilla = builder.AddCentraFlotilla("my-flotilla", tcpPort: 9100, udpPort: 9200, grpcPort: 9300, httpPort: 9301);

        flotilla.ShouldNotBeNull();
        flotilla.Resource.Name.ShouldBe("my-flotilla");
        flotilla.Resource.TryGetContainerImageName(out var imageName).ShouldBeTrue();
        imageName.ShouldBe("ghcr.io/chadahoochie/flotilla:0");

        flotilla.Resource.TcpEndpoint.EndpointName.ShouldBe(CentraFlotillaResource.TcpEndpointName);
        flotilla.Resource.UdpEndpoint.EndpointName.ShouldBe(CentraFlotillaResource.UdpEndpointName);
        flotilla.Resource.GrpcEndpoint.EndpointName.ShouldBe(CentraFlotillaResource.GrpcEndpointName);
        flotilla.Resource.HttpEndpoint.EndpointName.ShouldBe(CentraFlotillaResource.HttpEndpointName);

        flotilla.Resource.TryGetAnnotationsOfType<EndpointAnnotation>(out var endpoints).ShouldBeTrue();
        endpoints.Count().ShouldBe(4);

        flotilla.Resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var envCallbacks).ShouldBeTrue();
        envCallbacks.ShouldNotBeEmpty();
    }

    [Fact]
    public void WithCentraFlotilla_TypedResource_SetsEnvironmentVariables()
    {
        var builder = DistributedApplication.CreateBuilder();
        var flotilla = builder.AddCentraFlotilla("test-flotilla");
        var container = builder.AddContainer("my-app", "alpine");

        container.WithCentraFlotilla(flotilla);

        container.Resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var annotations).ShouldBeTrue();
        annotations.ShouldNotBeEmpty();
    }

    [Fact]
    public void WithCentraFlotilla_TypedResource_Null_ThrowsArgumentNullException()
    {
        var builder = DistributedApplication.CreateBuilder();
        var container = builder.AddContainer("my-app", "alpine");

        Should.Throw<ArgumentNullException>(() => container.WithCentraFlotilla((IResourceBuilder<CentraFlotillaResource>)null!));
    }
}
