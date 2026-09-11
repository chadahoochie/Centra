using System.Net;
using System.Text;
using System.Text.Json;
using Centra.Components;
using Centra.Sync;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Sync;

public sealed class ControlPlaneClientTests
{
    [Fact]
    public void Constructor_NullHttpClient_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new ControlPlaneClient(null!));
    }

    [Fact]
    public async Task Should_Get_Components_From_ControlPlane()
    {
        // Arrange
        var expected = new List<ComponentDefinition>
        {
            new()
            {
                Name = "orders-store",
                Type = ComponentType.StateStore,
                Provider = "in-memory"
            }
        };

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            req.RequestUri!.PathAndQuery.ShouldBe("/api/v1/components");
            req.Method.ShouldBe(HttpMethod.Get);

            var json = JsonSerializer.Serialize(expected);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://controlplane.local") };
        var client = new ControlPlaneClient(httpClient);

        // Act
        var result = await client.GetComponentsAsync();

        // Assert
        result.Count.ShouldBe(1);
        result.First().Name.ShouldBe("orders-store");
    }

    [Fact]
    public async Task Should_Get_Topology_From_ControlPlane()
    {
        var expected = new List<ServiceNodeDto>
        {
            new("orders-svc", "inst-1", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string>())
        };

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            req.RequestUri!.PathAndQuery.ShouldBe("/api/v1/topology");
            var json = JsonSerializer.Serialize(expected);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://controlplane.local") };
        var client = new ControlPlaneClient(httpClient);

        var result = await client.GetTopologyAsync();
        result.Count.ShouldBe(1);
        result.First().AppId.ShouldBe("orders-svc");
    }

    [Fact]
    public async Task Should_Get_Resilience_Policies_From_ControlPlane()
    {
        var expected = new List<ResiliencePolicyDto>
        {
            new() { PolicyName = "retry-policy", MaxRetries = 3 }
        };

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            req.RequestUri!.PathAndQuery.ShouldBe("/api/v1/resilience");
            var json = JsonSerializer.Serialize(expected);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://controlplane.local") };
        var client = new ControlPlaneClient(httpClient);

        var result = await client.GetResiliencePoliciesAsync();
        result.Count.ShouldBe(1);
        result.First().PolicyName.ShouldBe("retry-policy");
    }

    [Fact]
    public async Task Should_Send_Heartbeat_Successfully()
    {
        // Arrange
        string? capturedBody = null;
        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            req.RequestUri!.PathAndQuery.ShouldBe("/api/v1/heartbeat");
            req.Method.ShouldBe(HttpMethod.Post);
            capturedBody = await req.Content!.ReadAsStringAsync(ct);

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://controlplane.local") };
        var client = new ControlPlaneClient(httpClient);

        // Act
        var metadata = new Dictionary<string, string> { ["region"] = "us-east" };
        await client.SendHeartbeatAsync("orders-app", "inst-1", "Healthy", metadata);

        // Assert
        capturedBody.ShouldNotBeNull();
        capturedBody.ShouldContain("orders-app");
        capturedBody.ShouldContain("inst-1");
        capturedBody.ShouldContain("Healthy");
        capturedBody.ShouldContain("region");
    }

    [Fact]
    public async Task Should_Stream_Updates_Via_Sse()
    {
        // Arrange
        var ssePayload = "data: {\"action\":1,\"definition\":{\"name\":\"cache\",\"type\":0,\"provider\":\"in-memory\",\"version\":\"v1\",\"metadata\":{}},\"revision\":1,\"timestampUtc\":\"2026-09-05T20:00:00Z\"}\n\n";

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            req.RequestUri!.PathAndQuery.ShouldContain("api/v1/sync/stream");
            req.Headers.Accept.ToString().ShouldContain("text/event-stream");

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://controlplane.local") };
        var client = new ControlPlaneClient(httpClient);

        // Act
        var events = new List<ComponentSyncEventDto>();
        await foreach (var evt in client.StreamUpdatesAsync("orders-app", "inst-1"))
        {
            events.Add(evt);
            break;
        }

        // Assert
        events.Count.ShouldBe(1);
        events[0].Definition.ShouldNotBeNull();
        events[0].Definition!.Name.ShouldBe("cache");
    }

    [Fact]
    public async Task Should_Stream_Resilience_Updates_Via_Sse()
    {
        var ssePayload = "data: {\"action\":\"Upserted\",\"policyName\":\"dynamic-retry\",\"revision\":1,\"timestampUtc\":\"2026-09-05T20:00:00Z\"}\n\n";

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            req.RequestUri!.PathAndQuery.ShouldContain("api/v1/resilience/stream");
            req.Headers.Accept.ToString().ShouldContain("text/event-stream");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            });
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://controlplane.local") };
        var client = new ControlPlaneClient(httpClient);

        var events = new List<ResilienceSyncEventDto>();
        await foreach (var evt in client.StreamResilienceUpdatesAsync("orders-app", "inst-1"))
        {
            events.Add(evt);
            break;
        }

        events.Count.ShouldBe(1);
        events[0].PolicyName.ShouldBe("dynamic-retry");
        events[0].Action.ShouldBe("Upserted");
    }
}
