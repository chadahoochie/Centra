using Centra.Actors;
using Centra.Core.Actors;
using Centra.Core.Workflows;
using Centra.Events;
using Centra.Locks;
using Centra.Providers.InMemory.Extensions;
using Centra.PubSub;
using Centra.State;
using Centra.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraOptionsBuilderTests
{
    [Fact]
    public void Should_Bind_Hierarchical_Options_From_IConfiguration()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Centra:AppId"] = "enterprise-app",
            ["Centra:DefaultCloudEventMode"] = "Structured",
            ["Centra:ControlPlane:Endpoint"] = "https://controlplane.prod:8080",
            ["Centra:ControlPlane:ClusterId"] = "eu-central-1",
            ["Centra:State:DefaultStore"] = "pg-statestore",
            ["Centra:PubSub:DefaultStore"] = "rabbit-pubsub",
            ["Centra:Locks:DefaultStore"] = "redis-lockstore",
            ["Centra:Actors:ActorIdleTimeout"] = "00:20:00",
            ["Centra:Workflows:DefaultExecutionTimeout"] = "02:00:00"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCentra();
        services.AddCentraInMemory();

        var sp = services.BuildServiceProvider();

        var centraOptions = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
        centraOptions.AppId.ShouldBe("enterprise-app");
        centraOptions.DefaultCloudEventMode.ShouldBe(CloudEventMode.Structured);
        centraOptions.DefaultStateStore.ShouldBe("pg-statestore");
        centraOptions.DefaultPubSub.ShouldBe("rabbit-pubsub");
        centraOptions.DefaultLockStore.ShouldBe("redis-lockstore");
        centraOptions.ControlPlane.Endpoint.ShouldBe("https://controlplane.prod:8080");
        centraOptions.ControlPlane.ClusterId.ShouldBe("eu-central-1");

        var actorOptions = sp.GetRequiredService<IOptions<ActorOptions>>().Value;
        actorOptions.ActorIdleTimeout.ShouldBe(TimeSpan.FromMinutes(20));
        actorOptions.DefaultStateStore.ShouldBe("pg-statestore");
        actorOptions.DefaultLockStore.ShouldBe("redis-lockstore");

        var workflowOptions = sp.GetRequiredService<IOptions<WorkflowOptions>>().Value;
        workflowOptions.DefaultExecutionTimeout.ShouldBe(TimeSpan.FromHours(2));
        workflowOptions.DefaultStateStore.ShouldBe("pg-statestore");
        workflowOptions.DefaultPubSub.ShouldBe("rabbit-pubsub");
        workflowOptions.DefaultLockStore.ShouldBe("redis-lockstore");

        var stateOptions = sp.GetRequiredService<IOptions<CentraStateOptions>>().Value;
        stateOptions.DefaultStore.ShouldBe("pg-statestore");

        var pubSubOptions = sp.GetRequiredService<IOptions<CentraPubSubOptions>>().Value;
        pubSubOptions.DefaultStore.ShouldBe("rabbit-pubsub");

        var locksOptions = sp.GetRequiredService<IOptions<CentraLocksOptions>>().Value;
        locksOptions.DefaultStore.ShouldBe("redis-lockstore");
    }

    [Fact]
    public void Should_Cascade_Default_Stores_To_Actors_And_Workflows_When_Unconfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentra(options =>
        {
            options.DefaultStateStore = "cluster-statestore";
            options.DefaultPubSub = "cluster-pubsub";
            options.DefaultLockStore = "cluster-lockstore";
        });
        services.AddCentraInMemory();

        var sp = services.BuildServiceProvider();

        var actorOptions = sp.GetRequiredService<IOptions<ActorOptions>>().Value;
        actorOptions.DefaultStateStore.ShouldBe("cluster-statestore");
        actorOptions.DefaultLockStore.ShouldBe("cluster-lockstore");

        var workflowOptions = sp.GetRequiredService<IOptions<WorkflowOptions>>().Value;
        workflowOptions.DefaultStateStore.ShouldBe("cluster-statestore");
        workflowOptions.DefaultPubSub.ShouldBe("cluster-pubsub");
        workflowOptions.DefaultLockStore.ShouldBe("cluster-lockstore");
    }

    [Fact]
    public void Should_Preserve_Explicit_Store_Overrides()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddCentra(options =>
        {
            options.DefaultStateStore = "global-statestore";
            options.DefaultPubSub = "global-pubsub";
            options.DefaultLockStore = "global-lockstore";
        });
        services.AddCentraInMemory();

        builder.ConfigureActors(a => a.DefaultStateStore = "actor-specific-statestore");
        builder.ConfigureWorkflows(w =>
        {
            w.DefaultStateStore = "workflow-specific-statestore";
            w.DefaultPubSub = "workflow-specific-pubsub";
        });

        var sp = services.BuildServiceProvider();

        var actorOptions = sp.GetRequiredService<IOptions<ActorOptions>>().Value;
        actorOptions.DefaultStateStore.ShouldBe("actor-specific-statestore");
        actorOptions.DefaultLockStore.ShouldBe("global-lockstore");

        var workflowOptions = sp.GetRequiredService<IOptions<WorkflowOptions>>().Value;
        workflowOptions.DefaultStateStore.ShouldBe("workflow-specific-statestore");
        workflowOptions.DefaultPubSub.ShouldBe("workflow-specific-pubsub");
        workflowOptions.DefaultLockStore.ShouldBe("global-lockstore");
    }

    [Fact]
    public void Should_Fluent_Configure_Via_ICentraBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCentra()
            .WithAppId("builder-app")
            .WithCloudEventMode(CloudEventMode.Structured)
            .ConfigureControlPlane(cp => cp.Endpoint = "http://cp.local:5000")
            .ConfigureState(s => s.DefaultStore = "builder-state")
            .ConfigurePubSub(p => p.DefaultStore = "builder-pubsub")
            .ConfigureLocks(l => l.DefaultStore = "builder-locks")
            .ConfigureActors(a => a.ActorIdleTimeout = TimeSpan.FromMinutes(7))
            .ConfigureWorkflows(w => w.PollingInterval = TimeSpan.FromSeconds(3))
            .AddInMemory();

        var sp = services.BuildServiceProvider();

        var centraOptions = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
        centraOptions.AppId.ShouldBe("builder-app");
        centraOptions.DefaultCloudEventMode.ShouldBe(CloudEventMode.Structured);
        centraOptions.ControlPlane.Endpoint.ShouldBe("http://cp.local:5000");
        centraOptions.State.DefaultStore.ShouldBe("builder-state");
        centraOptions.PubSub.DefaultStore.ShouldBe("builder-pubsub");
        centraOptions.Locks.DefaultStore.ShouldBe("builder-locks");

        var actorOptions = sp.GetRequiredService<IOptions<ActorOptions>>().Value;
        actorOptions.ActorIdleTimeout.ShouldBe(TimeSpan.FromMinutes(7));
        actorOptions.DefaultStateStore.ShouldBe("builder-state");
        actorOptions.DefaultLockStore.ShouldBe("builder-locks");

        var workflowOptions = sp.GetRequiredService<IOptions<WorkflowOptions>>().Value;
        workflowOptions.PollingInterval.ShouldBe(TimeSpan.FromSeconds(3));
        workflowOptions.DefaultStateStore.ShouldBe("builder-state");
        workflowOptions.DefaultPubSub.ShouldBe("builder-pubsub");
        workflowOptions.DefaultLockStore.ShouldBe("builder-locks");
    }

    [Fact]
    public void Should_Register_Components_Via_ICentraBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCentra()
            .AddActor<SampleBuilderActor, ISampleBuilderActor>()
            .AddWorkflow<SampleBuilderWorkflow>()
            .AddWorkflowActivity<SampleBuilderActivity>()
            .AddInMemory();

        var sp = services.BuildServiceProvider();

        var actorRegistrations = sp.GetServices<ActorRegistration>();
        actorRegistrations.ShouldContain(r => r.ActorType == typeof(SampleBuilderActor) && r.InterfaceType == typeof(ISampleBuilderActor));

        var workflowRegistry = sp.GetRequiredService<IWorkflowRegistry>();
        workflowRegistry.TryGetWorkflow(typeof(SampleBuilderWorkflow).Name, out _).ShouldBeTrue();
        workflowRegistry.TryGetActivity(typeof(SampleBuilderActivity).Name, out _).ShouldBeTrue();
    }

    [Fact]
    public void Should_Support_AddCentraBuilder_Action_Syntax()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCentraBuilder(centra =>
        {
            centra.WithAppId("action-builder-app")
                .ConfigureActors(a => a.ActorIdleTimeout = TimeSpan.FromMinutes(8))
                .ConfigureWorkflows(w => w.PollingInterval = TimeSpan.FromSeconds(4))
                .AddInMemory();
        });

        var sp = services.BuildServiceProvider();

        var centraOptions = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
        centraOptions.AppId.ShouldBe("action-builder-app");

        var actorOptions = sp.GetRequiredService<IOptions<ActorOptions>>().Value;
        actorOptions.ActorIdleTimeout.ShouldBe(TimeSpan.FromMinutes(8));

        var workflowOptions = sp.GetRequiredService<IOptions<WorkflowOptions>>().Value;
        workflowOptions.PollingInterval.ShouldBe(TimeSpan.FromSeconds(4));
    }
}
