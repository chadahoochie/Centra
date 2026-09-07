using System.Net;
using System.Net.Http.Json;
using Centra.Components;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Endpoints;

public sealed class ControlPlaneBindingsEndpointsTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public ControlPlaneBindingsEndpointsTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddCentraControlPlane();

        _app = builder.Build();
        _app.MapCentraControlPlaneEndpoints();
        _app.StartAsync().GetAwaiter().GetResult();

        _client = _app.GetTestServer().CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Should_Return_Empty_Bindings_List_Initially()
    {
        var response = await _client.GetAsync("/api/v1/bindings");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<ComponentDefinition>>();
        list.ShouldNotBeNull();
        list.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_Filter_And_Return_Only_Binding_Components()
    {
        // 1. Register a StateStore component
        var stateStore = new ComponentDefinition
        {
            Name = "redis-state",
            Type = ComponentType.StateStore,
            Provider = "redis"
        };
        await _client.PostAsJsonAsync("/api/v1/components", stateStore);

        // 2. Register a Binding component
        var webhookBinding = new ComponentDefinition
        {
            Name = "order-webhook",
            Type = ComponentType.Binding,
            Provider = "http",
            Metadata = new Dictionary<string, string> { ["url"] = "https://hooks.example.com" }
        };
        await _client.PostAsJsonAsync("/api/v1/components", webhookBinding);

        // 3. Query /api/v1/bindings
        var response = await _client.GetAsync("/api/v1/bindings");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<ComponentDefinition>>();
        list.ShouldNotBeNull();
        list.Count.ShouldBe(1);
        list[0].Name.ShouldBe("order-webhook");
        list[0].Type.ShouldBe(ComponentType.Binding);
        list[0].Metadata.ShouldNotBeNull();
        list[0].Metadata["url"].ShouldBe("https://hooks.example.com");
    }
}
