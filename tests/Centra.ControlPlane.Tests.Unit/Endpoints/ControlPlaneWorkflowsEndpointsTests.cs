using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.Core.Workflows;
using Centra.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Endpoints;

public sealed class ControlPlaneWorkflowsEndpointsTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly IWorkflowEngine _engine = Substitute.For<IWorkflowEngine>();

    public ControlPlaneWorkflowsEndpointsTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddCentraControlPlane();

        var registry = new WorkflowRegistry();
        registry.RegisterWorkflow(new WorkflowDefinition("OrderFulfillmentWorkflow", typeof(DummyWorkflow), typeof(string), typeof(bool)));
        registry.RegisterActivity(new WorkflowActivityDefinition("ChargeCreditCardActivity", typeof(DummyActivity), typeof(decimal), typeof(string)));

        builder.Services.AddSingleton<IWorkflowRegistry>(registry);
        builder.Services.AddSingleton(_engine);

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
    public async Task Should_Return_Registered_Workflow_Definitions()
    {
        var response = await _client.GetAsync("/api/v1/workflows/definitions");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var defs = await response.Content.ReadFromJsonAsync<JsonArray>();
        defs.ShouldNotBeNull();
        defs.Count.ShouldBe(1);
        defs[0]?["name"]?.GetValue<string>().ShouldBe("OrderFulfillmentWorkflow");
    }

    [Fact]
    public async Task Should_Return_Registered_Activity_Definitions()
    {
        var response = await _client.GetAsync("/api/v1/workflows/activities");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var acts = await response.Content.ReadFromJsonAsync<JsonArray>();
        acts.ShouldNotBeNull();
        acts.Count.ShouldBe(1);
        acts[0]?["name"]?.GetValue<string>().ShouldBe("ChargeCreditCardActivity");
    }

    [Fact]
    public async Task Should_Return_Workflow_Instance_State()
    {
        var id = new WorkflowInstanceId("wf-inst-777");
        var state = new WorkflowState(
            id,
            "OrderFulfillmentWorkflow",
            WorkflowStatus.Running,
            ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty,
            "ProcessingPayment",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

        _engine.GetWorkflowStateAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WorkflowState?>(state));

        var response = await _client.GetAsync($"/api/v1/workflows/instances/{id.Value}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var obj = await response.Content.ReadFromJsonAsync<JsonObject>();
        obj.ShouldNotBeNull();
        obj["instanceId"]?.GetValue<string>().ShouldBe(id.Value);
        obj["status"]?.GetValue<string>().ShouldBe("Running");
        obj["customStatus"]?.GetValue<string>().ShouldBe("ProcessingPayment");
    }

    [Fact]
    public async Task Should_Return_Workflow_Instance_History()
    {
        var id = new WorkflowInstanceId("wf-inst-888");
        var history = new List<WorkflowHistoryEventRecord>
        {
            new() { EventId = 1, EventType = (int)WorkflowHistoryEventType.WorkflowStarted, Name = "OrderFulfillmentWorkflow", Timestamp = DateTimeOffset.UtcNow }
        };

        _engine.GetWorkflowHistoryAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<List<WorkflowHistoryEventRecord>>(history));

        var response = await _client.GetAsync($"/api/v1/workflows/instances/{id.Value}/history");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<WorkflowHistoryEventRecord>>();
        list.ShouldNotBeNull();
        list.Count.ShouldBe(1);
        list[0].Name.ShouldBe("OrderFulfillmentWorkflow");
    }
}

public sealed class DummyWorkflow : Workflow<string, bool>
{
    public override ValueTask<bool> RunAsync(IWorkflowContext context, string input) => ValueTask.FromResult(true);
}

public sealed class DummyActivity : WorkflowActivity<decimal, string>
{
    public override ValueTask<string> RunAsync(WorkflowActivityContext context, decimal input) => ValueTask.FromResult("charged");
}
