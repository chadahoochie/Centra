using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Centra.Aspire.Hosting;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

/// <summary>
/// Asserts the Flotilla server resource is launchable as a container.
/// </summary>
public sealed class CentraFlotillaLaunchabilityTests
{
    [Fact]
    public void AddCentraFlotilla_DefaultTagIsZero()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddCentraFlotilla().Resource.TryGetContainerImageName(out var imageName).ShouldBeTrue();

        imageName.ShouldNotBeNull();
        imageName[(imageName.LastIndexOf(':') + 1)..].ShouldBe("0");
    }

    [Fact]
    public void AddCentraFlotilla_ProducesAResourceTheOrchestratorCanStart()
    {
        var builder = DistributedApplication.CreateBuilder();

        var flotilla = builder.AddCentraFlotilla();

        flotilla.Resource.ShouldBeAssignableTo<ContainerResource>();
    }

    [Fact]
    public void AddCentraFlotilla_ResolvesAFullyQualifiedContainerImage()
    {
        var builder = DistributedApplication.CreateBuilder();

        var flotilla = builder.AddCentraFlotilla();

        flotilla.Resource.TryGetContainerImageName(out var imageName).ShouldBeTrue();
        imageName.ShouldBe("ghcr.io/chadahoochie/flotilla:0");
    }

    [Fact]
    public void AddCentraFlotilla_HonoursAnOverriddenRegistryAndTag()
    {
        var builder = DistributedApplication.CreateBuilder();

        var flotilla = builder.AddCentraFlotilla()
            .WithImageRegistry("internal.registry.example")
            .WithImageTag("2.3.4");

        flotilla.Resource.TryGetContainerImageName(out var imageName).ShouldBeTrue();
        imageName.ShouldBe("internal.registry.example/chadahoochie/flotilla:2.3.4");
    }

    [Fact]
    public void AddCentraFlotilla_OptsIntoOtlpExportSoTelemetryReachesTheDashboard()
    {
        var builder = DistributedApplication.CreateBuilder();

        var flotilla = builder.AddCentraFlotilla();

        flotilla.Resource
            .TryGetAnnotationsOfType<OtlpExporterAnnotation>(out _)
            .ShouldBeTrue();
    }

    [Fact]
    public void AddCentraFlotilla_ExposesAllEndpointsWithCorrectPorts()
    {
        var builder = DistributedApplication.CreateBuilder();

        var flotilla = builder.AddCentraFlotilla(
            tcpPort: 19100,
            udpPort: 19200,
            grpcPort: 19300,
            httpPort: 19301);

        flotilla.Resource.TryGetAnnotationsOfType<EndpointAnnotation>(out var endpoints).ShouldBeTrue();
        var endpointList = endpoints.ToList();
        endpointList.Count.ShouldBe(4);

        var tcp = endpointList.Single(e => e.Name == CentraFlotillaResource.TcpEndpointName);
        tcp.Port.ShouldBe(19100);
        tcp.TargetPort.ShouldBe(9100);
        tcp.IsProxied.ShouldBeFalse();

        var udp = endpointList.Single(e => e.Name == CentraFlotillaResource.UdpEndpointName);
        udp.Port.ShouldBe(19200);
        udp.TargetPort.ShouldBe(9200);
        udp.IsProxied.ShouldBeFalse();

        var grpc = endpointList.Single(e => e.Name == CentraFlotillaResource.GrpcEndpointName);
        grpc.Port.ShouldBe(19300);
        grpc.TargetPort.ShouldBe(9300);
        grpc.IsProxied.ShouldBeFalse();

        var http = endpointList.Single(e => e.Name == CentraFlotillaResource.HttpEndpointName);
        http.Port.ShouldBe(19301);
        http.TargetPort.ShouldBe(9301);
    }

    [Fact]
    public void AddCentraFlotilla_SupportsWithDockerfile()
    {
        var builder = DistributedApplication.CreateBuilder();

        var flotilla = builder.AddCentraFlotilla()
            .WithDockerfile("../../..", "samples/FlotillaSimulation/Server/Dockerfile");

        flotilla.Resource.TryGetAnnotationsOfType<DockerfileBuildAnnotation>(out var annotations).ShouldBeTrue();
        annotations.ShouldNotBeEmpty();
    }
}
