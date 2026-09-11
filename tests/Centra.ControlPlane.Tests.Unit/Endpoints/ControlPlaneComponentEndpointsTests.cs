using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Centra.Components;
using Centra.ControlPlane.Catalog;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.ControlPlane.Sync;
using Centra.ControlPlane.Topology;
using Centra.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Endpoints;

public sealed class ControlPlaneComponentEndpointsTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public ControlPlaneComponentEndpointsTests()
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
    public async Task Should_Return_Empty_Components_Initially()
    {
        var response = await _client.GetAsync("/api/v1/components");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var components = await response.Content.ReadFromJsonAsync<List<ComponentDefinition>>();
        components.ShouldNotBeNull();
        components.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_Create_And_Get_Component()
    {
        var component = new ComponentDefinition
        {
            Name = "redis-state-1",
            Type = ComponentType.StateStore,
            Provider = "redis",
            Metadata = new Dictionary<string, string> { ["host"] = "localhost:6379" }
        };

        var postResponse = await _client.PostAsJsonAsync("/api/v1/components", component);
        postResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var entry = await postResponse.Content.ReadFromJsonAsync<ComponentCatalogEntry>();
        entry.ShouldNotBeNull();
        entry.Definition.Name.ShouldBe("redis-state-1");

        var getResponse = await _client.GetAsync("/api/v1/components/redis-state-1");
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var retrieved = await getResponse.Content.ReadFromJsonAsync<ComponentDefinition>();
        retrieved.ShouldNotBeNull();
        retrieved.Name.ShouldBe("redis-state-1");
        retrieved.Type.ShouldBe(ComponentType.StateStore);
        retrieved.Metadata.ShouldNotBeNull();
        retrieved.Metadata["host"].ShouldBe("localhost:6379");
    }

    [Fact]
    public async Task Should_Return_NotFound_When_Component_Does_Not_Exist()
    {
        var response = await _client.GetAsync("/api/v1/components/nonexistent-component");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_Update_Existing_Component()
    {
        var component1 = new ComponentDefinition
        {
            Name = "pubsub-main",
            Type = ComponentType.PubSub,
            Provider = "redis"
        };
        await _client.PostAsJsonAsync("/api/v1/components", component1);

        var component2 = new ComponentDefinition
        {
            Name = "pubsub-main",
            Type = ComponentType.PubSub,
            Provider = "rabbitmq"
        };
        var updateResponse = await _client.PostAsJsonAsync("/api/v1/components", component2);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var getResponse = await _client.GetAsync("/api/v1/components/pubsub-main");
        var updated = await getResponse.Content.ReadFromJsonAsync<ComponentDefinition>();
        updated.ShouldNotBeNull();
        updated.Provider.ShouldBe("rabbitmq");
    }

    [Fact]
    public async Task Should_Delete_Component_And_Handle_Subsequent_Delete()
    {
        var component = new ComponentDefinition
        {
            Name = "temp-component",
            Type = ComponentType.DistributedLock,
            Provider = "redis"
        };
        await _client.PostAsJsonAsync("/api/v1/components", component);

        var deleteResponse = await _client.DeleteAsync("/api/v1/components/temp-component");
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var secondDelete = await _client.DeleteAsync("/api/v1/components/temp-component");
        secondDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var getResponse = await _client.GetAsync("/api/v1/components/temp-component");
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_Record_Heartbeat_And_Return_Topology()
    {
        var heartbeat = new HeartbeatRequest("test-service", "instance-1", "Ready", new Dictionary<string, string> { ["zone"] = "us-east" });

        var response = await _client.PostAsJsonAsync("/api/v1/heartbeat", heartbeat);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var hbResponse = await response.Content.ReadFromJsonAsync<HeartbeatResponse>();
        hbResponse.Acknowledged.ShouldBeTrue();

        var topoResponse = await _client.GetAsync("/api/v1/topology");
        topoResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var nodes = await topoResponse.Content.ReadFromJsonAsync<List<ClientNodeInfo>>();
        nodes.ShouldNotBeNull();
        nodes.Count.ShouldBe(1);
        nodes[0].AppId.ShouldBe("test-service");
        nodes[0].InstanceId.ShouldBe("instance-1");
        nodes[0].Status.ShouldBe("Ready");
    }

    [Fact]
    public async Task Should_Return_Healthy_Status_From_Health_Endpoint()
    {
        var response = await _client.GetAsync("/api/v1/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var node = await response.Content.ReadFromJsonAsync<JsonObject>();
        node.ShouldNotBeNull();
        node["status"]?.GetValue<string>().ShouldBe("Healthy");
        node["service"]?.GetValue<string>().ShouldBe("Centra.ControlPlane");
    }

    [Fact]
    public async Task Should_Stream_Sync_Events_Via_Sse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var streamResponse = await _client.GetAsync("/api/v1/sync/stream?appId=test-app&instanceId=node-1", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        streamResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        streamResponse.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var dispatcher = _app.Services.GetRequiredService<IComponentSyncDispatcher>() as ComponentSyncDispatcher;
        while (dispatcher?.SubscriberCount == 0 && !cts.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }

        var newComp = new ComponentDefinition
        {
            Name = "streamed-component",
            Type = ComponentType.StateStore,
            Provider = "memory"
        };
        await _client.PostAsJsonAsync("/api/v1/components", newComp, cts.Token);

        var stream = await streamResponse.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var line = await reader.ReadLineAsync(cts.Token);

        line.ShouldNotBeNull();
        line.ShouldStartWith("data: ");
        line.ShouldContain("streamed-component");
    }
}
