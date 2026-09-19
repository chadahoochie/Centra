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
/// <remarks>
/// Scope: these assertions cover the application model only. They prove the resource carries a
/// container image the orchestrator can resolve and start, and they run offline with no Docker
/// daemon and no registry access. They deliberately do <em>not</em> prove the default tag is
/// present in the registry — that is a registry fact, not a code fact, and it depends on the
/// <c>Publish Control Plane Image</c> workflow having been run for the current version.
/// </remarks>
public sealed class CentraControlPlaneLaunchabilityTests
{
    [Fact]
    public void CentraControlPlaneImage_DefaultTagTracksTheFrameworkVersion()
    {
        var frameworkVersion = typeof(CentraControlPlaneImage).Assembly.GetName().Version;

        frameworkVersion.ShouldNotBeNull();
        CentraControlPlaneImage.Tag.ShouldBe(
            $"{frameworkVersion.Major}.{frameworkVersion.Minor}.{frameworkVersion.Build}",
            "The published image tag must match the version Centra ships its assemblies and "
            + "packages under (VersionPrefix in Directory.Build.props, recorded in RELEASE_NOTES.md), "
            + "so whoever runs the publish workflow pushes the tag the resource actually pulls.");
    }

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
    public void AddCentraControlPlane_OptsIntoOtlpExportSoTelemetryReachesTheDashboard()
    {
        var builder = DistributedApplication.CreateBuilder();

        var controlPlane = builder.AddCentraControlPlane();

        controlPlane.Resource
            .TryGetAnnotationsOfType<OtlpExporterAnnotation>(out _)
            .ShouldBeTrue(
                "Aspire injects OTLP exporter configuration into project resources automatically "
                + "but not into containers, so without an explicit opt-in the control plane would "
                + "export to its own loopback inside the container and the dashboard would show "
                + "no traces, metrics, or logs for it.");
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
