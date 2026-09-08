using System.Net;
using System.Net.Http.Json;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.ControlPlane.Sync;
using Centra.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Resilience;

public sealed class ResilienceEndpointsTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public ResilienceEndpointsTests()
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
    public async Task Should_Return_Empty_List_Initially()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/resilience");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<ResiliencePolicyDto>>();
        list.ShouldNotBeNull();
        list.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_Create_And_Retrieve_Resilience_Policy()
    {
        // Arrange
        var policy = new ResiliencePolicyDto
        {
            PolicyName = "payment-policy",
            MaxRetries = 3,
            BackoffType = "Exponential",
            BaseDelayMs = 100,
            MaxDelayMs = 2000,
            FailureRatio = 0.5,
            SamplingDurationSeconds = 10,
            MinimumThroughput = 5,
            BreakDurationSeconds = 5,
            TimeoutSeconds = 10
        };

        // Act - Create
        var postResponse = await _client.PostAsJsonAsync("/api/v1/resilience", policy);

        // Assert - Create
        postResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await postResponse.Content.ReadFromJsonAsync<ResiliencePolicyDto>();
        created.ShouldNotBeNull();
        created.PolicyName.ShouldBe("payment-policy");
        created.MaxRetries.ShouldBe(3);

        // Act - Get
        var getResponse = await _client.GetAsync("/api/v1/resilience/payment-policy");

        // Assert - Get
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var retrieved = await getResponse.Content.ReadFromJsonAsync<ResiliencePolicyDto>();
        retrieved.ShouldNotBeNull();
        retrieved.PolicyName.ShouldBe("payment-policy");
        retrieved.MaxRetries.ShouldBe(3);
        retrieved.TimeoutSeconds.ShouldBe(10);
    }

    [Fact]
    public async Task Should_Return_NotFound_When_Policy_Does_Not_Exist()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/resilience/unknown-policy");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_Delete_Policy_And_Return_NoContent()
    {
        // Arrange
        var policy = new ResiliencePolicyDto
        {
            PolicyName = "delete-me",
            MaxRetries = 1
        };
        await _client.PostAsJsonAsync("/api/v1/resilience", policy);

        // Act
        var deleteResponse = await _client.DeleteAsync("/api/v1/resilience/delete-me");
        var secondDelete = await _client.DeleteAsync("/api/v1/resilience/delete-me");
        var getResponse = await _client.GetAsync("/api/v1/resilience/delete-me");

        // Assert
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        secondDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_Stream_Resilience_Updates_Via_Sse()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var streamResponse = await _client.GetAsync("/api/v1/resilience/stream?appId=test-app&instanceId=node-1", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        streamResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        streamResponse.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        // Wait until subscriber is registered in dispatcher
        var dispatcher = _app.Services.GetRequiredService<IComponentSyncDispatcher>() as ComponentSyncDispatcher;
        while (dispatcher?.ResilienceSubscriberCount == 0 && !cts.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }

        // Act: Upsert a policy while stream is open
        var policy = new ResiliencePolicyDto
        {
            PolicyName = "streamed-policy",
            MaxRetries = 2
        };
        await _client.PostAsJsonAsync("/api/v1/resilience", policy, cts.Token);

        // Read from stream
        var stream = await streamResponse.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var line = await reader.ReadLineAsync(cts.Token);

        // Assert
        line.ShouldNotBeNull();
        line.ShouldStartWith("data: ");
        line.ShouldContain("streamed-policy");
    }
}
