using Centra.Core.Workflows;
using Centra.Workflows;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class WorkflowClientTests
{
    private readonly IWorkflowEngine _engine = Substitute.For<IWorkflowEngine>();
    private readonly IWorkflowRegistry _registry = new WorkflowRegistry();

    [Fact]
    public async Task StartWorkflowAsync_Typed_Should_Resolve_Name_From_Attribute_And_Delegate_To_Engine()
    {
        // Arrange
        var client = new WorkflowClient(_engine, _registry);
        var expectedId = new WorkflowInstanceId("wf-custom-123");

        _engine.StartWorkflowAsync("CustomOnboardingWorkflow", Arg.Any<object?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WorkflowInstanceId>(expectedId));

        // Act
        var resultId = await client.StartWorkflowAsync<AttributedCustomerWorkflow, string>("customer-42", "wf-custom-123");

        // Assert
        resultId.ShouldBe(expectedId);
        await _engine.Received(1).StartWorkflowAsync("CustomOnboardingWorkflow", "customer-42", "wf-custom-123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartWorkflowAsync_Typed_Without_Attribute_Should_Use_Type_Name()
    {
        // Arrange
        var client = new WorkflowClient(_engine, _registry);
        var expectedId = new WorkflowInstanceId("wf-auto-456");

        _engine.StartWorkflowAsync(nameof(NonAttributedWorkflow), Arg.Any<object?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WorkflowInstanceId>(expectedId));

        // Act
        var resultId = await client.StartWorkflowAsync<NonAttributedWorkflow, int>(100);

        // Assert
        resultId.ShouldBe(expectedId);
        await _engine.Received(1).StartWorkflowAsync(nameof(NonAttributedWorkflow), 100, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartWorkflowAsync_Untyped_Should_Delegate_Directly_To_Engine()
    {
        // Arrange
        var client = new WorkflowClient(_engine, _registry);
        var expectedId = new WorkflowInstanceId("wf-untyped-789");

        _engine.StartWorkflowAsync("RawWorkflow", "some-input", "inst-1", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WorkflowInstanceId>(expectedId));

        // Act
        var resultId = await client.StartWorkflowAsync("RawWorkflow", "some-input", "inst-1");

        // Assert
        resultId.ShouldBe(expectedId);
        await _engine.Received(1).StartWorkflowAsync("RawWorkflow", "some-input", "inst-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWorkflowHistoryAsync_Should_Map_Records_To_WorkflowHistoryEvents()
    {
        // Arrange
        var client = new WorkflowClient(_engine, _registry);
        var id = new WorkflowInstanceId("wf-history");
        var now = DateTimeOffset.UtcNow;

        var records = new List<WorkflowHistoryEventRecord>
        {
            new()
            {
                EventId = 1,
                EventType = (int)WorkflowHistoryEventType.WorkflowStarted,
                Name = "TestWorkflow",
                Timestamp = now,
                Data = [1, 2, 3],
                Details = "Started successfully"
            },
            new()
            {
                EventId = 2,
                EventType = (int)WorkflowHistoryEventType.WorkflowCompleted,
                Name = "TestWorkflow",
                Timestamp = now.AddMinutes(1),
                Data = null,
                Details = null
            }
        };

        _engine.GetWorkflowHistoryAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<List<WorkflowHistoryEventRecord>>(records));

        // Act
        var events = await client.GetWorkflowHistoryAsync(id);

        // Assert
        events.Count.ShouldBe(2);
        events[0].EventId.ShouldBe(1);
        events[0].EventType.ShouldBe(WorkflowHistoryEventType.WorkflowStarted);
        events[0].Name.ShouldBe("TestWorkflow");
        events[0].Timestamp.ShouldBe(now);
        events[0].Data.ToArray().ShouldBe(new byte[] { 1, 2, 3 });
        events[0].Details.ShouldBe("Started successfully");

        events[1].EventId.ShouldBe(2);
        events[1].EventType.ShouldBe(WorkflowHistoryEventType.WorkflowCompleted);
        events[1].Data.IsEmpty.ShouldBeTrue();
        events[1].Details.ShouldBeNull();
    }

    [Fact]
    public async Task Client_Methods_Should_Delegate_Correctly()
    {
        // Arrange
        var client = new WorkflowClient(_engine, _registry);
        var id = new WorkflowInstanceId("wf-test");

        var state = new WorkflowState(id, "TestWf", WorkflowStatus.Running, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        _engine.GetWorkflowStateAsync(id, Arg.Any<CancellationToken>()).Returns(new ValueTask<WorkflowState?>(state));
        _engine.WaitForWorkflowCompletionAsync<string>(id, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<string>("completed-output"));

        // Act & Assert GetWorkflowStateAsync
        var retrievedState = await client.GetWorkflowStateAsync(id);
        retrievedState.ShouldNotBeNull();
        retrievedState.Value.Status.ShouldBe(WorkflowStatus.Running);

        // Act & Assert WaitForWorkflowCompletionAsync
        var output = await client.WaitForWorkflowCompletionAsync<string>(id);
        output.ShouldBe("completed-output");

        // Act & Assert RaiseEventAsync
        await client.RaiseEventAsync(id, "PaymentApproved", new { TxId = "tx-1" });
        await _engine.Received(1).RaiseEventAsync(id, "PaymentApproved", Arg.Any<object>(), Arg.Any<CancellationToken>());

        // Act & Assert TerminateWorkflowAsync
        await client.TerminateWorkflowAsync(id, "User cancelled");
        await _engine.Received(1).TerminateWorkflowAsync(id, "User cancelled", Arg.Any<CancellationToken>());

        // Act & Assert PurgeWorkflowAsync
        await client.PurgeWorkflowAsync(id);
        await _engine.Received(1).PurgeWorkflowAsync(id, Arg.Any<CancellationToken>());
    }

    [Workflow("CustomOnboardingWorkflow")]
    private sealed class AttributedCustomerWorkflow : Workflow<string, bool>
    {
        public override ValueTask<bool> RunAsync(IWorkflowContext context, string input)
        {
            return ValueTask.FromResult(true);
        }
    }

    private sealed class NonAttributedWorkflow : Workflow<int, int>
    {
        public override ValueTask<int> RunAsync(IWorkflowContext context, int input)
        {
            return ValueTask.FromResult(input * 2);
        }
    }
}
