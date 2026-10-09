using Centra;
using Centra.Actors;
using Centra.Core.Actors;
using Centra.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorPlacementMultiClusterTests
{
    [Fact]
    public void ActorPlacementRing_Should_Only_Include_Nodes_In_Same_Cluster_And_AppId()
    {
        var services = new ServiceCollection();

        var centraOptions = new CentraOptions
        {
            AppId = "orders-svc",
            ControlPlane = new CentraControlPlaneOptions
            {
                ClusterId = "cluster-alpha",
                InstanceId = "alpha-node-1"
            }
        };

        services.AddSingleton(centraOptions);
        services.AddSingleton(Options.Create(centraOptions));

        var fakeTopology = new FakeClusterTopologyProvider(new[]
        {
            // Same cluster, same app -> should be in ring
            new ServiceNodeDto("orders-svc", "alpha-node-2", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "cluster-alpha"),
            // Different cluster, same app -> must NOT be in ring
            new ServiceNodeDto("orders-svc", "beta-node-1", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "cluster-beta"),
            // Same cluster, different app -> must NOT be in ring
            new ServiceNodeDto("billing-svc", "alpha-node-3", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "cluster-alpha")
        });

        services.AddSingleton<IClusterTopologyProvider>(fakeTopology);
        services.AddCentraActors();

        var sp = services.BuildServiceProvider();
        var placementDirector = (ActorPlacementDirector)sp.GetRequiredService<IActorPlacementDirector>();

        placementDirector.Ring.Nodes.ShouldContain("alpha-node-1");
        placementDirector.Ring.Nodes.ShouldContain("alpha-node-2");
        placementDirector.Ring.Nodes.ShouldNotContain("beta-node-1");
        placementDirector.Ring.Nodes.ShouldNotContain("alpha-node-3");
    }
}
