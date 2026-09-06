using System.Net.Http.Json;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.ControlPlane.Topology;
using Centra.Drivers;
using Centra.Hosting.Extensions;
using Centra.Locks;
using Centra.Providers.InMemory.Extensions;
using Centra.Providers.InMemory.Locks;
using Centra.Sample.MultiInstance.Simulation;
using Centra.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.MultiInstance;

public sealed class MultiInstanceClusterIntegrationTests
{
    [Fact]
    public async Task Should_Execute_Multi_Instance_Cluster_Simulation_Successfully()
    {
        // Act
        var result = await MultiInstanceDemoRunner.RunAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ClusterName.ShouldBe("centra-multi-instance-cluster");
        result.NodeCount.ShouldBe(3);
        result.LeaderElectionSuccessful.ShouldBeTrue();
        result.ElectedLeaderInstanceId.ShouldNotBeNull();
        result.ConcurrencyConflictResolved.ShouldBeTrue();
        result.FinalCounterValue.ShouldBe(115);
        result.TotalTasksProcessed.ShouldBe(3);
        result.ElapsedDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Should_Register_All_Replicas_In_ControlPlane_Topology()
    {
        // 1. Arrange & Start Control Plane
        var cpBuilder = WebApplication.CreateBuilder();
        cpBuilder.WebHost.UseTestServer();
        cpBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        cpBuilder.Services.AddCentraControlPlane();

        var cpApp = cpBuilder.Build();
        cpApp.MapCentraControlPlaneEndpoints();
        await cpApp.StartAsync();

        var cpHttpClient = cpApp.GetTestServer().CreateClient();

        // 2. Start two distinct node instances
        var hostA = CreateTestHost("node-alpha", cpHttpClient);
        var hostB = CreateTestHost("node-beta", cpHttpClient);

        await hostA.StartAsync();
        await hostB.StartAsync();

        // 3. Poll topology from Control Plane until heartbeats register
        List<ClientNodeInfo>? nodes = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            nodes = await cpHttpClient.GetFromJsonAsync<List<ClientNodeInfo>>("api/v1/topology");
            if (nodes is not null && nodes.Count >= 2)
            {
                break;
            }
            await Task.Delay(50);
        }

        nodes.ShouldNotBeNull();
        nodes.Count.ShouldBeGreaterThanOrEqualTo(2);
        nodes.ShouldContain(n => n.InstanceId == "node-alpha" && n.Status == "Healthy");
        nodes.ShouldContain(n => n.InstanceId == "node-beta" && n.Status == "Healthy");

        // 4. Cleanup
        await hostA.StopAsync();
        await hostB.StopAsync();
        await cpApp.StopAsync();
    }

    [Fact]
    public async Task Should_Enforce_Mutual_Exclusion_When_Multiple_Instances_Compete_For_Lock()
    {
        // Arrange
        var sharedLockDriver = new InMemoryDistributedLockDriver();

        var host1 = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddCentra(opt => opt.AppId = "worker-1");
                services.AddCentraInMemory();
                services.AddSingleton(sharedLockDriver);
                services.AddSingleton<IDistributedLockDriver>(sp => sp.GetRequiredService<InMemoryDistributedLockDriver>());
            })
            .Build();

        var host2 = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddCentra(opt => opt.AppId = "worker-2");
                services.AddCentraInMemory();
                services.AddSingleton(sharedLockDriver);
                services.AddSingleton<IDistributedLockDriver>(sp => sp.GetRequiredService<InMemoryDistributedLockDriver>());
            })
            .Build();

        await host1.StartAsync();
        await host2.StartAsync();

        var lockProvider1 = host1.Services.GetRequiredService<IDistributedLockProvider>();
        var lockProvider2 = host2.Services.GetRequiredService<IDistributedLockProvider>();

        // Act 1: Instance 1 acquires lock
        var lock1 = await lockProvider1.TryAcquireLockAsync("lockstore", "exclusive-resource", TimeSpan.FromSeconds(10));
        lock1.ShouldNotBeNull();

        // Act 2: Instance 2 attempts to acquire same lock -> must be rejected
        var lock2 = await lockProvider2.TryAcquireLockAsync("lockstore", "exclusive-resource", TimeSpan.FromSeconds(10));
        lock2.ShouldBeNull();

        // Act 3: Instance 1 releases lock
        await lock1.DisposeAsync();

        // Act 4: Instance 2 acquires lock -> succeeds
        var lock2Retry = await lockProvider2.TryAcquireLockAsync("lockstore", "exclusive-resource", TimeSpan.FromSeconds(10));
        lock2Retry.ShouldNotBeNull();
        await lock2Retry.DisposeAsync();

        await host1.StopAsync();
        await host2.StopAsync();
    }

    private static IHost CreateTestHost(string instanceId, HttpClient cpClient)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddCentra(options =>
                {
                    options.AppId = "multi-instance-service";
                    options.ControlPlaneEndpoint = "http://localhost/";
                    options.ControlPlane.InstanceId = instanceId;
                    options.ControlPlane.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
                });
                services.AddCentraInMemory();
                services.AddSingleton<IControlPlaneClient>(new ControlPlaneClient(cpClient));
            })
            .Build();
    }
}
