using Centra.Workflows;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class WorkflowAbstractionsTests
{
    [Fact]
    public void WorkflowInstanceId_Should_Validate_And_Support_Implicit_Conversions()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => new WorkflowInstanceId(""));
        Should.Throw<ArgumentException>(() => new WorkflowInstanceId("   "));

        var id = new WorkflowInstanceId("wf-12345");
        id.Value.ShouldBe("wf-12345");
        id.ToString().ShouldBe("wf-12345");

        string raw = id;
        raw.ShouldBe("wf-12345");

        WorkflowInstanceId fromString = "wf-67890";
        fromString.Value.ShouldBe("wf-67890");

        var autoId = WorkflowInstanceId.New();
        autoId.Value.ShouldStartWith("wf-");
        autoId.Value.Length.ShouldBe(35); // "wf-" + 32 hex chars
    }

    [Fact]
    public void WorkflowStatus_Should_Support_All_Lifecycle_States()
    {
        Enum.GetValues<WorkflowStatus>().Length.ShouldBe(6);
        ((int)WorkflowStatus.Pending).ShouldBe(0);
        ((int)WorkflowStatus.Running).ShouldBe(1);
        ((int)WorkflowStatus.Completed).ShouldBe(2);
        ((int)WorkflowStatus.Failed).ShouldBe(3);
        ((int)WorkflowStatus.Terminated).ShouldBe(4);
        ((int)WorkflowStatus.Suspended).ShouldBe(5);
    }

    [Fact]
    public void WorkflowState_Should_Store_Properties_Correctly()
    {
        var id = new WorkflowInstanceId("wf-test-01");
        var createdAt = DateTimeOffset.UtcNow;
        var updatedAt = createdAt.AddMinutes(1);
        var input = new byte[] { 1, 2, 3 };
        var output = new byte[] { 4, 5, 6 };

        var state = new WorkflowState(
            id,
            "OrderWorkflow",
            WorkflowStatus.Running,
            input,
            output,
            "AwaitingPayment",
            createdAt,
            updatedAt,
            null);

        state.InstanceId.ShouldBe(id);
        state.WorkflowName.ShouldBe("OrderWorkflow");
        state.Status.ShouldBe(WorkflowStatus.Running);
        state.Input.ToArray().ShouldBe(input);
        state.Output.ToArray().ShouldBe(output);
        state.CustomStatus.ShouldBe("AwaitingPayment");
        state.CreatedAt.ShouldBe(createdAt);
        state.LastUpdatedAt.ShouldBe(updatedAt);
        state.FailureDetails.ShouldBeNull();
    }

    [Fact]
    public void WorkflowHistoryEvent_Should_Capture_Event_Details()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var data = new byte[] { 10, 20 };
        var historyEvent = new WorkflowHistoryEvent(
            1,
            WorkflowHistoryEventType.ActivityCompleted,
            "ReserveStock",
            timestamp,
            data,
            "Stock reserved successfully");

        historyEvent.EventId.ShouldBe(1);
        historyEvent.EventType.ShouldBe(WorkflowHistoryEventType.ActivityCompleted);
        historyEvent.Name.ShouldBe("ReserveStock");
        historyEvent.Timestamp.ShouldBe(timestamp);
        historyEvent.Data.ToArray().ShouldBe(data);
        historyEvent.Details.ShouldBe("Stock reserved successfully");
    }

    [Fact]
    public void WorkflowActivityContext_Should_Initialize_Properly()
    {
        using var cts = new CancellationTokenSource();
        var id = new WorkflowInstanceId("wf-act-ctx");
        var context = new WorkflowActivityContext(id, "ProcessPayment", cts.Token);

        context.InstanceId.ShouldBe(id);
        context.ActivityName.ShouldBe("ProcessPayment");
        context.CancellationToken.ShouldBe(cts.Token);
    }

    [Fact]
    public void WorkflowAttribute_And_WorkflowActivityAttribute_Should_Validate()
    {
        Should.Throw<ArgumentException>(() => new WorkflowAttribute(""));
        Should.Throw<ArgumentException>(() => new WorkflowActivityAttribute("   "));

        var wfAttr = new WorkflowAttribute("CustomerOnboarding");
        wfAttr.Name.ShouldBe("CustomerOnboarding");

        var actAttr = new WorkflowActivityAttribute("SendWelcomeEmail");
        actAttr.Name.ShouldBe("SendWelcomeEmail");
    }
}
