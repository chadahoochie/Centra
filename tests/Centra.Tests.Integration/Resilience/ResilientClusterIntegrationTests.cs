using System.Net.Http.Json;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.ControlPlane.Sync;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Resilience;
using Centra.Sample.Resilience.Simulation;
using Centra.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.Resilience;

public sealed class ResilientClusterIntegrationTests
{
    [Fact]
    public async Task Should_Execute_End_To_End_Resilience_And_Chaos_Simulation_Successfully()
    {
        // Act
        var result = await ResilienceDemoRunner.RunAsync();

        // Assert
        result.ShouldNotBeNull();
        result.BaselineSuccess.ShouldBeTrue();
        result.TransientRetrySuccess.ShouldBeTrue();
        result.TransientRetryAttempts.ShouldBe(3);
        result.CircuitBreakerTripped.ShouldBeTrue();
        result.CircuitBreakerRecovered.ShouldBeTrue();
        result.TimeoutTriggered.ShouldBeTrue();
        result.DynamicPolicyUpdateApplied.ShouldBeTrue();
        result.ElapsedDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Should_Synchronize_Resilience_Policies_Live_From_ControlPlane_To_Client()
    {
        // 1. Start In-Process Control Plane
        var cpBuilder = WebApplication.CreateBuilder();
        cpBuilder.WebHost.UseTestServer();
        cpBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        cpBuilder.Services.AddCentraControlPlane();

        var cpApp = cpBuilder.Build();
        cpApp.MapCentraControlPlaneEndpoints();
        await cpApp.StartAsync();

        var cpHttpClient = cpApp.GetTestServer().CreateClient();

        // 2. Pre-seed a resilience policy in the Control Plane
        var initialPolicy = new ResiliencePolicyDto
        {
            PolicyName = "invocation:orders-db",
            MaxRetries = 4,
            BackoffType = "Exponential",
            BaseDelayMs = 100,
            TimeoutSeconds = 5
        };
        var postRes = await cpHttpClient.PostAsJsonAsync("/api/v1/resilience", initialPolicy);
        postRes.EnsureSuccessStatusCode();

        // 3. Start Client Node connected to Control Plane Sync
        var clientBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
                services.AddCentra(opt =>
                {
                    opt.AppId = "client-node-1";
                    opt.ControlPlaneEndpoint = "http://localhost/";
                    opt.ControlPlane.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
                });
                services.AddCentraInMemory();

                services.AddSingleton<IControlPlaneClient>(new ControlPlaneClient(cpHttpClient));
            });

        using var clientHost = clientBuilder.Build();
        await clientHost.StartAsync();

        var registry = clientHost.Services.GetRequiredService<IResiliencePolicyRegistry>();

        // 4. Verify initial pre-seeded policy was synced on startup
        CentraResiliencePolicyDefinition? syncedPolicy = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            syncedPolicy = registry.GetPolicy("invocation:orders-db");
            if (syncedPolicy is not null)
            {
                break;
            }
            await Task.Delay(50);
        }

        syncedPolicy.ShouldNotBeNull();
        syncedPolicy.PolicyName.ShouldBe("invocation:orders-db");
        syncedPolicy.Retry.ShouldNotBeNull();
        syncedPolicy.Retry.MaxRetries.ShouldBe(4);

        // Ensure SSE stream is established and subscribed
        var dispatcher = cpApp.Services.GetRequiredService<IComponentSyncDispatcher>() as ComponentSyncDispatcher;
        if (dispatcher is not null)
        {
            var waitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (dispatcher.ResilienceSubscriberCount == 0 && DateTime.UtcNow < waitDeadline)
            {
                await Task.Delay(20);
            }
        }

        // 5. Publish a live update to Control Plane
        var updatedPolicy = initialPolicy with
        {
            MaxRetries = 8
        };
        var updateRes = await cpHttpClient.PostAsJsonAsync("/api/v1/resilience", updatedPolicy);
        updateRes.EnsureSuccessStatusCode();

        // 6. Verify client dynamically updated policy via SSE stream
        CentraResiliencePolicyDefinition? hotReloadedPolicy = null;
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            hotReloadedPolicy = registry.GetPolicy("invocation:orders-db");
            if (hotReloadedPolicy?.Retry?.MaxRetries == 8)
            {
                break;
            }
            await Task.Delay(50);
        }

        hotReloadedPolicy.ShouldNotBeNull();
        hotReloadedPolicy.Retry!.MaxRetries.ShouldBe(8);

        // Cleanup
        await clientHost.StopAsync();
        await cpApp.StopAsync();
    }
}
