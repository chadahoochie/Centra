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
}

[Workflow("CustomOnboardingWorkflow")]
public sealed class AttributedCustomerWorkflow : Workflow<string, bool>
{
    public override ValueTask<bool> RunAsync(IWorkflowContext context, string input)
    {
        return ValueTask.FromResult(true);
    }
}

public sealed class NonAttributedWorkflow : Workflow<int, int>
{
    public override ValueTask<int> RunAsync(IWorkflowContext context, int input)
    {
        return ValueTask.FromResult(input * 2);
    }
}
