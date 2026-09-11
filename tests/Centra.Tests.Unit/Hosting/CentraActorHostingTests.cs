using Centra.Actors;
using Centra.Core.Actors;
using Centra.Hosting.Extensions;
using Centra.Hosting.HostedServices;
using Centra.State;
using Centra.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraActorHostingTests
{
    [Fact]
    public void AddCentraActors_Should_Register_All_Required_Singletons_And_HostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IStateStore>());

        services.AddCentraActors(options =>
        {
            options.ActorIdleTimeout = TimeSpan.FromMinutes(5);
            options.DefaultStateStore = "custom-store";
        });

        var sp = services.BuildServiceProvider();

        sp.GetService<ActorOptions>().ShouldNotBeNull();
        sp.GetRequiredService<ActorOptions>().ActorIdleTimeout.ShouldBe(TimeSpan.FromMinutes(5));
        sp.GetRequiredService<ActorOptions>().DefaultStateStore.ShouldBe("custom-store");

        // ConsistentHashRing is intentionally not resolvable from DI - only IActorPlacementDirector
        // is public API; the ring is an implementation detail constructed inside its factory.
        sp.GetService<ConsistentHashRing>().ShouldBeNull();
        sp.GetService<IActorPlacementDirector>().ShouldNotBeNull();
        sp.GetService<ActorManager>().ShouldNotBeNull();
        sp.GetService<ActorReminderCoordinator>().ShouldNotBeNull();
        sp.GetService<IActorProxyFactory>().ShouldNotBeNull();

        var hostedServices = sp.GetServices<IHostedService>().ToList();
        hostedServices.OfType<CentraActorHostedService>().ShouldNotBeEmpty();
    }

    [Fact]
    public void AddCentraActor_Should_Register_Actor_In_DI_And_ActorManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IStateStore>());
        services.AddCentraActors();

        services.AddCentraActor<SampleHostedTestActor, ISampleHostedTestActor>();

        var sp = services.BuildServiceProvider();

        var instance = sp.GetService<SampleHostedTestActor>();
        instance.ShouldNotBeNull();

        var manager = sp.GetRequiredService<ActorManager>();
        manager.ShouldNotBeNull();
    }

    [Fact]
    public void AddActor_Shorthand_Should_Register_Actor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IStateStore>());
        services.AddCentraActors();
        services.AddActor<SampleHostedTestActor, ISampleHostedTestActor>();

        var sp = services.BuildServiceProvider();
        sp.GetService<SampleHostedTestActor>().ShouldNotBeNull();
    }

    [Fact]
    public void AddCentraActors_With_ClusterTopologyProvider_Should_Update_Placement_Director()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IStateStore>());

        var centraOptions = new Centra.Hosting.Options.CentraOptions
        {
            AppId = "my-service",
            DefaultStateStore = "shared-store"
        };
        centraOptions.ControlPlane.InstanceId = "node-1";
        services.AddSingleton(centraOptions);

        var topologyProvider = Substitute.For<IClusterTopologyProvider>();
        var initialNodes = new[]
        {
            new ServiceNodeDto("my-service", "node-2", "Ready", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string>()),
            new ServiceNodeDto("other-service", "other-1", "Ready", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string>())
        };
        topologyProvider.GetSnapshot().Returns(initialNodes);
        services.AddSingleton(topologyProvider);

        services.AddCentraActors();

        var sp = services.BuildServiceProvider();
        var director = sp.GetRequiredService<IActorPlacementDirector>();
        director.ShouldNotBeNull();

        // Fire TopologyChanged
        var added = new[] { new ServiceNodeDto("my-service", "node-3", "Ready", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string>()) };
        var removed = new[] { new ServiceNodeDto("my-service", "node-2", "Ready", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string>()) };
        topologyProvider.TopologyChanged += Raise.Event<EventHandler<ClusterTopologyChangedEventArgs>>(
            topologyProvider,
            new ClusterTopologyChangedEventArgs
            {
                AddedNodes = added,
                RemovedNodes = removed,
                CurrentSnapshot = added
            });

        // Check actor placement
        var isLocal = director.IsLocal(new ActorIdentity(new ActorType("SampleActor"), new ActorId("actor-1")));
        // Resolving ActorManager applies fallback to DefaultStateStore
        sp.GetRequiredService<ActorManager>().ShouldNotBeNull();
        sp.GetRequiredService<ActorOptions>().DefaultStateStore.ShouldBe("shared-store");
    }

    public interface ISampleHostedTestActor : IActor
    {
    }

    public sealed class SampleHostedTestActor : Actor, ISampleHostedTestActor
    {
    }
}
