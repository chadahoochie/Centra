using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraWorkflowEndpointsTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public CentraWorkflowEndpointsTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddCentra(options => options.AppId = "wf-endpoint-app");
        builder.Services.AddCentraInMemory();
        builder.Services.AddCentraWorkflows();
        builder.Services.AddCentraWorkflow<EndpointEchoWorkflow>();
        builder.Services.AddCentraWorkflowActivity<EchoActivity>();

        _app = builder.Build();
        _app.MapCentraEndpoints();
        _app.StartAsync().GetAwaiter().GetResult();

        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Should_Start_Workflow_Via_Http_And_Retrieve_State_And_History()
    {
        // 1. POST /centra/workflows/EndpointEchoWorkflow/start with body
        var startResponse = await _client.PostAsJsonAsync("/centra/workflows/EndpointEchoWorkflow/start", "HelloWorkflow");
        startResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var startNode = await startResponse.Content.ReadFromJsonAsync<JsonObject>();
        startNode.ShouldNotBeNull();
        var instanceId = startNode["instanceId"]?.GetValue<string>();
        instanceId.ShouldNotBeNull();

        // 2. GET /centra/workflows/{instanceId}
        var stateResponse = await _client.GetAsync($"/centra/workflows/{instanceId}");
        stateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var stateNode = await stateResponse.Content.ReadFromJsonAsync<JsonObject>();
        stateNode.ShouldNotBeNull();
        stateNode["instanceId"]?.GetValue<string>().ShouldBe(instanceId);
        stateNode["workflowName"]?.GetValue<string>().ShouldBe("EndpointEchoWorkflow");
        stateNode["status"]?.GetValue<string>().ShouldBe("Completed");

        // 3. GET /centra/workflows/{instanceId}/history
        var histResponse = await _client.GetAsync($"/centra/workflows/{instanceId}/history");
        histResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var histArray = await histResponse.Content.ReadFromJsonAsync<JsonArray>();
        histArray.ShouldNotBeNull();
        histArray.Count.ShouldBeGreaterThan(2);

        // 4. POST /centra/workflows/{instanceId}/raise-event/TestEvent
        var eventResponse = await _client.PostAsJsonAsync($"/centra/workflows/{instanceId}/raise-event/TestEvent", "payload");
        eventResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // 5. DELETE /centra/workflows/{instanceId}
        var deleteResponse = await _client.DeleteAsync($"/centra/workflows/{instanceId}");
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify purged
        var getAfterPurge = await _client.GetAsync($"/centra/workflows/{instanceId}");
        getAfterPurge.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_Start_Workflow_With_Explicit_Instance_Id()
    {
        var explicitId = $"explicit-wf-{Guid.NewGuid():N}";
        var startResponse = await _client.PostAsJsonAsync($"/centra/workflows/EndpointEchoWorkflow/{explicitId}/start", "HelloExplicit");
        startResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var node = await startResponse.Content.ReadFromJsonAsync<JsonObject>();
        node.ShouldNotBeNull();
        node["instanceId"]?.GetValue<string>().ShouldBe(explicitId);

        var stateResponse = await _client.GetAsync($"/centra/workflows/{explicitId}");
        stateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_Terminate_Workflow_Via_Http()
    {
        var explicitId = $"term-wf-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync($"/centra/workflows/EndpointEchoWorkflow/{explicitId}/start", "RunToTerminate");

        var termResponse = await _client.PostAsJsonAsync($"/centra/workflows/{explicitId}/terminate", "Aborting test");
        termResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

public sealed class EndpointEchoWorkflow : Workflow<string, string>
{
    public override async ValueTask<string> RunAsync(IWorkflowContext context, string input)
    {
        var result = await context.CallActivityAsync<EchoActivity, string, string>(input);
        return result;
    }
}

public sealed class EchoActivity : WorkflowActivity<string, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, string input)
    {
        return ValueTask.FromResult($"echo:{input}");
    }
}
