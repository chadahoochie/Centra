using Centra.Actors;
using Centra.Core.Actors;
using Centra.Drivers;
using Centra.Hosting.Extensions;
using Centra.Locks;
using Centra.Providers.InMemory.Bindings;
using Centra.Providers.InMemory.Extensions;
using Centra.Providers.InMemory.Locks;
using Centra.Providers.InMemory.PubSub;
using Centra.Providers.InMemory.State;
using Centra.Registry;
using Centra.Sample.Actors.Domain;
using Centra.Sample.Actors.Simulation;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.Actors;

public sealed class ActorClusterIntegrationTests
{
    [Fact]
    public async Task Should_Execute_Virtual_Actor_Simulation_Successfully()
    {
        // Act
        var result = await ActorDemoRunner.RunAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ActorId.ShouldBe("acc-42");
        result.ConcurrentDepositsCount.ShouldBe(50);
        result.PassivationAndReactivationSucceeded.ShouldBeTrue();
        result.ReminderExecuted.ShouldBeTrue();
        result.FinalBalance.ShouldBe(577.50m);
        result.ElapsedDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Should_Route_Actors_Across_Consistent_Hash_Nodes()
    {
        // Arrange
        var ring = new ConsistentHashRing(virtualNodesPerNode: 100);
        ring.AddNode("node-alpha");
        ring.AddNode("node-beta");
        ring.AddNode("node-gamma");

        var actorType = ActorType.FromType<AccountActor>();
        var nodeCounts = new Dictionary<string, int>
        {
            ["node-alpha"] = 0,
            ["node-beta"] = 0,
            ["node-gamma"] = 0
        };

        // Act: Assign 150 actors
        for (var i = 0; i < 150; i++)
        {
            var identity = new ActorIdentity(actorType, $"acc-{i}");
            var node = ring.GetNode(identity.ToString());
            nodeCounts[node]++;

            // Verify determinism: repeating the call yields the exact same node
            ring.GetNode(identity.ToString()).ShouldBe(node);
        }

        // Assert: every node received a reasonable share of partitions
        nodeCounts["node-alpha"].ShouldBeGreaterThan(20);
        nodeCounts["node-beta"].ShouldBeGreaterThan(20);
        nodeCounts["node-gamma"].ShouldBeGreaterThan(20);
    }

    [Fact]
    public async Task Should_Persist_State_Across_Nodes_With_Shared_Store()
    {
        // Arrange: shared in-memory state store across 2 cluster nodes
        var sharedStateDriver = new InMemoryStateStoreDriver();

        var host1 = CreateActorHost("node-1", sharedStateDriver);
        var host2 = CreateActorHost("node-2", sharedStateDriver);

        await host1.StartAsync();
        await host2.StartAsync();

        try
        {
            var proxyFactory1 = host1.Services.GetRequiredService<IActorProxyFactory>();
            var proxyFactory2 = host2.Services.GetRequiredService<IActorProxyFactory>();
            var actorManager1 = host1.Services.GetRequiredService<ActorManager>();

            var accountId = new ActorId("acc-shared-99");
            var proxy1 = proxyFactory1.CreateActorProxy<IAccountActor>(accountId);

            // Act 1: Node 1 deposits $200
            var balance1 = await proxy1.DepositAsync(200m);
            balance1.ShouldBe(200m);

            // Act 2: Node 1 passivates the actor, flushing state to shared store
            var identity = new ActorIdentity(ActorType.FromType<AccountActor>(), accountId);
            var passivated = await actorManager1.PassivateActorAsync(identity);
            passivated.ShouldBeTrue();
            actorManager1.ActiveCount.ShouldBe(0);

            // Act 3: Node 2 accesses the actor -> reads persisted state seamlessly
            var proxy2 = proxyFactory2.CreateActorProxy<IAccountActor>(accountId);
            var balance2 = await proxy2.GetBalanceAsync();
            balance2.ShouldBe(200m);

            // Act 4: Node 2 deposits additional $100 -> new balance $300
            var balance3 = await proxy2.DepositAsync(100m);
            balance3.ShouldBe(300m);
        }
        finally
        {
            await host1.StopAsync();
            await host2.StopAsync();
            host1.Dispose();
            host2.Dispose();
        }
    }

    [Fact]
    public async Task Should_Coordinate_Durable_Reminders_Across_Cluster_With_Distributed_Lock()
    {
        // Arrange: shared state store and lock driver across 2 cluster nodes
        var sharedStateDriver = new InMemoryStateStoreDriver();
        var sharedLockDriver = new InMemoryDistributedLockDriver();

        var host1 = CreateActorHost("node-1", sharedStateDriver, sharedLockDriver);
        var host2 = CreateActorHost("node-2", sharedStateDriver, sharedLockDriver);

        await host1.StartAsync();
        await host2.StartAsync();

        try
        {
            var proxyFactory1 = host1.Services.GetRequiredService<IActorProxyFactory>();
            var coordinator1 = host1.Services.GetRequiredService<ActorReminderCoordinator>();
            var coordinator2 = host2.Services.GetRequiredService<ActorReminderCoordinator>();

            var accountId = new ActorId("acc-rem-cluster");
            var proxy1 = proxyFactory1.CreateActorProxy<IAccountActor>(accountId);

            // Deposit initial $1000
            await proxy1.DepositAsync(1000m);

            // Register reminder on Node 1 & Node 2 due immediately
            var identity = new ActorIdentity(ActorType.FromType<AccountActor>(), accountId);
            coordinator1.RegisterReminder(identity, AccountActor.InterestReminderName, TimeSpan.Zero, TimeSpan.FromMinutes(1), null);
            coordinator2.RegisterReminder(identity, AccountActor.InterestReminderName, TimeSpan.Zero, TimeSpan.FromMinutes(1), null);

            // Act: Both nodes simultaneously execute coordinator.TickAsync()
            var tick1Task = coordinator1.TickAsync().AsTask();
            var tick2Task = coordinator2.TickAsync().AsTask();

            var ticks = await Task.WhenAll(tick1Task, tick2Task);

            // Assert: Exactly ONE node acquired the distributed lock and executed the reminder
            var totalExecuted = ticks[0] + ticks[1];
            totalExecuted.ShouldBe(1);

            // Balance has 5% interest applied exactly once ($1000 * 1.05 = $1050)
            var finalBalance = await proxy1.GetBalanceAsync();
            finalBalance.ShouldBe(1050m);
        }
        finally
        {
            await host1.StopAsync();
            await host2.StopAsync();
            host1.Dispose();
            host2.Dispose();
        }
    }

    private static IHost CreateActorHost(
        string instanceId,
        InMemoryStateStoreDriver sharedStateDriver,
        InMemoryDistributedLockDriver? sharedLockDriver = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddCentra(options =>
                {
                    options.AppId = "cluster-actor-service";
                    options.DefaultStateStore = "shared-statestore";
                    options.DefaultLockStore = "shared-lockstore";
                    options.ControlPlane.InstanceId = instanceId;
                });

                services.AddCentraInMemory(
                    defaultStateStore: "shared-statestore",
                    defaultLockStore: "shared-lockstore");

                // Replace state store driver with shared instance
                services.AddSingleton(sharedStateDriver);
                services.AddSingleton<IStateStoreDriver>(sp => sp.GetRequiredService<InMemoryStateStoreDriver>());

                if (sharedLockDriver != null)
                {
                    services.AddSingleton(sharedLockDriver);
                    services.AddSingleton<IDistributedLockDriver>(sp => sp.GetRequiredService<InMemoryDistributedLockDriver>());
                }

                services.AddSingleton<IComponentInitializer>(sp => new InMemoryComponentInitializer(
                    sharedStateDriver,
                    sp.GetRequiredService<InMemoryPubSubDriver>(),
                    sharedLockDriver ?? sp.GetRequiredService<InMemoryDistributedLockDriver>(),
                    sp.GetRequiredService<InMemoryBindingDriver>(),
                    defaultStateStore: "shared-statestore",
                    defaultPubSub: "pubsub",
                    defaultLockStore: "shared-lockstore"));

                services.AddCentraActors(options =>
                {
                    options.DefaultStateStore = "shared-statestore";
                    options.DefaultLockStore = "shared-lockstore";
                    options.ActorIdleTimeout = TimeSpan.FromSeconds(5);
                    options.ReminderInterval = TimeSpan.FromHours(1);
                });

                services.AddActor<AccountActor, IAccountActor>();
            })
            .Build();
    }
}
