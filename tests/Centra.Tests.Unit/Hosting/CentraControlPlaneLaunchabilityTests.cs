using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Centra.Aspire.Hosting;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

/// <summary>
/// Asserts the control-plane resource is <em>launchable</em> — that the orchestrator has
/// something to start — rather than merely addressable. A bare <see cref="Resource"/> carrying
/// an endpoint annotation satisfies every addressability assertion while publishing an endpoint
/// for a process that never exists.
/// </summary>
public sealed class CentraControlPlaneLaunchabilityTests
{
    [Fact]
    public void AddCentraControlPlane_ProducesAResourceTheOrchestratorCanStart()
    {
        var builder = DistributedApplication.CreateBuilder();

        var controlPlane = builder.AddCentraControlPlane();

        controlPlane.Resource.ShouldBeAssignableTo<ContainerResource>();
    }

    [Fact]
    public void AddCentraControlPlane_ResolvesAFullyQualifiedContainerImage()
    {
        var builder = DistributedApplication.CreateBuilder();

        var controlPlane = builder.AddCentraControlPlane();

        controlPlane.Resource.TryGetContainerImageName(out var imageName).ShouldBeTrue();
        imageName.ShouldBe(
            $"{CentraControlPlaneImage.Registry}/{CentraControlPlaneImage.Image}:{CentraControlPlaneImage.Tag}");
    }

    [Fact]
    public void AddCentraControlPlane_HonoursAnOverriddenRegistryAndTag()
    {
        var builder = DistributedApplication.CreateBuilder();

        var controlPlane = builder.AddCentraControlPlane()
            .WithImageRegistry("internal.registry.example")
            .WithImageTag("2.3.4");

        controlPlane.Resource.TryGetContainerImageName(out var imageName).ShouldBeTrue();
        imageName.ShouldBe($"internal.registry.example/{CentraControlPlaneImage.Image}:2.3.4");
    }

    [Fact]
    public void AddCentraControlPlane_ExposesTheContainerHttpPortThroughTheHttpEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder();

        var controlPlane = builder.AddCentraControlPlane(port: 18080);

        controlPlane.Resource.TryGetAnnotationsOfType<EndpointAnnotation>(out var endpoints).ShouldBeTrue();
        var http = endpoints.ShouldHaveSingleItem();
        http.Name.ShouldBe(CentraControlPlaneResource.HttpEndpointName);
        http.Port.ShouldBe(18080);
        http.TargetPort.ShouldBe(CentraControlPlaneImage.ContainerHttpPort);
    }
}
