using System.Net.Http.Json;
using Centra.Components;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.ControlPlane.Sync;
using Centra.ControlPlane.Topology;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.ControlPlane;

public sealed class ControlPlaneLiveSyncIntegrationTests
{
    [Fact]
    public async Task Should_Sync_Initial_And_Dynamic_HotReload_Components_From_ControlPlane()
    {
        // 1. Arrange & Start Control Plane in TestServer
        var cpBuilder = WebApplication.CreateBuilder();
        cpBuilder.WebHost.UseTestServer();
        cpBuilder.Services.AddCentraControlPlane();

        var cpApp = cpBuilder.Build();
        cpApp.MapCentraControlPlaneEndpoints();
        await cpApp.StartAsync();

        var cpHttpClient = cpApp.GetTestServer().CreateClient();

        // 2. Pre-register an initial component in Control Plane
        var initialDef = new ComponentDefinition
        {
            Name = "orders-statestore",
            Type = ComponentType.StateStore,
            Provider = "in-memory"
        };
        var initialPostResponse = await cpHttpClient.PostAsJsonAsync("api/v1/components", initialDef);
        initialPostResponse.EnsureSuccessStatusCode();

        // 3. Configure and Start Runtime Microservice Generic Host
        var runtimeHostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddCentra(options =>
                {
                    options.AppId = "orders-service";
                    options.ControlPlaneEndpoint = "http://localhost/";
                    options.ControlPlane.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
                });
                services.AddCentraInMemory();

                // Point ControlPlaneClient to the TestServer HTTP client
                services.AddSingleton<IControlPlaneClient>(new ControlPlaneClient(cpHttpClient));
            });

        using var runtimeHost = runtimeHostBuilder.Build();
        await runtimeHost.StartAsync();

        var runtimeRegistry = runtimeHost.Services.GetRequiredService<IComponentRegistry>();

        // 4. Verify Initial Component was automatically synced on startup
        var initialSynced = await WaitForComponentAsync(runtimeRegistry, "orders-statestore", TimeSpan.FromSeconds(3));
        initialSynced.ShouldNotBeNull();
        initialSynced.Name.ShouldBe("orders-statestore");

        // 5. Setup dynamic update listener on runtime registry
        var hotReloadTcs = new TaskCompletionSource<ComponentDefinition>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtimeRegistry.ComponentUpdated += def =>
        {
            if (def.Name == "events-pubsub")
            {
                hotReloadTcs.TrySetResult(def);
            }
        };

        // Ensure SSE stream is established and subscribed
        var dispatcher = cpApp.Services.GetRequiredService<IComponentSyncDispatcher>() as ComponentSyncDispatcher;
        if (dispatcher is not null)
        {
            var waitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (dispatcher.SubscriberCount == 0 && DateTime.UtcNow < waitDeadline)
            {
                await Task.Delay(20);
            }
        }

        // 6. Act: Operator adds a brand-new component to the Control Plane via REST API
        var dynamicDef = new ComponentDefinition
        {
            Name = "events-pubsub",
            Type = ComponentType.PubSub,
            Provider = "in-memory"
        };
        var dynamicPostResponse = await cpHttpClient.PostAsJsonAsync("api/v1/components", dynamicDef);
        dynamicPostResponse.EnsureSuccessStatusCode();

        // 7. Assert: Live SSE stream pushed the new component to runtime without restart
        var completedTask = await Task.WhenAny(hotReloadTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completedTask.ShouldBe(hotReloadTcs.Task, "Timed out waiting for dynamic hot-reload via SSE stream");

        var hotReloadedComponent = await hotReloadTcs.Task;
        hotReloadedComponent.Name.ShouldBe("events-pubsub");
        hotReloadedComponent.Type.ShouldBe(ComponentType.PubSub);

        var registeredInRegistry = runtimeRegistry.GetComponent("events-pubsub");
        registeredInRegistry.ShouldNotBeNull();
        registeredInRegistry.Name.ShouldBe("events-pubsub");

        // 8. Verify Topology Heartbeat was recorded
        await Task.Delay(150); // Allow heartbeat timer to fire
        var topologyResponse = await cpHttpClient.GetFromJsonAsync<List<ClientNodeInfo>>("api/v1/topology");
        topologyResponse.ShouldNotBeNull();
        topologyResponse.ShouldContain(n => n.AppId == "orders-service" && n.Status == "Healthy");

        // Cleanup
        await runtimeHost.StopAsync();
        await cpApp.StopAsync();
    }

    private static async Task<ComponentDefinition?> WaitForComponentAsync(
        IComponentRegistry registry,
        string name,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var comp = registry.GetComponent(name);
            if (comp is not null)
            {
                return comp;
            }
            await Task.Delay(20);
        }
        return registry.GetComponent(name);
    }
}
