using Centra.Actors;
using Centra.Core.Actors;
using Centra.Hosting.Extensions;
using Centra.Hosting.HostedServices;
using Centra.State;
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

    public interface ISampleHostedTestActor : IActor
    {
    }

    public sealed class SampleHostedTestActor : Actor, ISampleHostedTestActor
    {
    }
}
