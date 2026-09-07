using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Centra.Actors;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Endpoints;

public sealed class ControlPlaneActorEndpointsTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public ControlPlaneActorEndpointsTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddCentraControlPlane();
        builder.Services.AddSingleton(new ActorRegistration(typeof(SampleActor), typeof(ISampleActor)));

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
    public async Task Should_Return_Registered_Actor_Types()
    {
        var response = await _client.GetAsync("/api/v1/actors/types");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var types = await response.Content.ReadFromJsonAsync<List<string>>();
        types.ShouldNotBeNull();
        types.ShouldContain("SampleActor");
    }

    [Fact]
    public async Task Should_Return_Active_Actor_Activations_Count()
    {
        var response = await _client.GetAsync("/api/v1/actors/activations");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var node = await response.Content.ReadFromJsonAsync<JsonObject>();
        node.ShouldNotBeNull();
        node["activeCount"]!.GetValue<int>().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Should_Passivate_Actor_Via_Post()
    {
        var response = await _client.PostAsync("/api/v1/actors/SampleActor/guest-1/passivate", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var node = await response.Content.ReadFromJsonAsync<JsonObject>();
        node.ShouldNotBeNull();
        node.ContainsKey("passivated").ShouldBeTrue();
    }

    public interface ISampleActor : IActor { }
    public sealed class SampleActor : Actor, ISampleActor { }
}
