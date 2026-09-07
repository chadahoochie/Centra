using Centra.Workflows;

namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Long-running workflow that waits for an external human-in-the-loop manager approval event via CloudEvents.
/// </summary>
public sealed class ManagerApprovalWorkflow : Workflow<ApprovalRequest, ApprovalResponse>
{
    public const string DecisionEventName = "ManagerDecision";

    public override async ValueTask<ApprovalResponse> RunAsync(
        IWorkflowContext context,
        ApprovalRequest input)
    {
        context.SetCustomStatus($"AwaitingManagerDecision: Request {input.RequestId} for ${input.Amount:F2}");

        // Suspends until external CloudEvent arrives
        var decision = await context.WaitForExternalEventAsync<ApprovalResponse>(DecisionEventName)
            .ConfigureAwait(false);

        context.SetCustomStatus(decision.Approved ? "Approved" : "Rejected");
        return decision;
    }
}
