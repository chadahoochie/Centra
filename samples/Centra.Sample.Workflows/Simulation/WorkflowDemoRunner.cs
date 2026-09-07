using System.Diagnostics;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Sample.Workflows.Domain;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Centra.Sample.Workflows.Simulation;

/// <summary>
/// Orchestrates an end-to-end interactive simulation of Centra Workflows and Sagas.
/// </summary>
public static class WorkflowDemoRunner
{
    public static async Task<WorkflowSimulationResult> RunAsync(
        string[]? args = null,
        CancellationToken cancellationToken = default)
    {
        var logs = new List<string>();
        void Log(string message)
        {
            var line = $"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}";
            logs.Add(line);
            Console.WriteLine(line);
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine(" CENTRA DISTRIBUTED WORKFLOWS & SAGAS RUNTIME ENGINE (DURABLE TASK ORCHESTRATOR) ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        var stopwatch = Stopwatch.StartNew();

        Log("Building host with Centra Workflows and in-memory providers...");
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddCentra(options =>
                {
                    options.AppId = "workflow-demo-service";
                    options.DefaultStateStore = "statestore";
                    options.DefaultLockStore = "lockstore";
                });
                services.AddCentraInMemory();
                services.AddCentraWorkflows(options =>
                {
                    options.DefaultStateStore = "statestore";
                    options.DefaultLockStore = "lockstore";
                });

                // Workflows
                services.AddWorkflow<OrderProcessingWorkflow>();
                services.AddWorkflow<ManagerApprovalWorkflow>();

                // Activities
                services.AddWorkflowActivity<ValidateOrderActivity>();
                services.AddWorkflowActivity<ReserveInventoryActivity>();
                services.AddWorkflowActivity<ReleaseInventoryCompensationActivity>();
                services.AddWorkflowActivity<ProcessPaymentActivity>();
                services.AddWorkflowActivity<ShipOrderActivity>();
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        var client = host.Services.GetRequiredService<IWorkflowClient>();

        // --- PHASE 1: HAPPY PATH ORDER WORKFLOW (WITH DURABLE TIMER) ---
        Console.ForegroundColor = ConsoleColor.Green;
        Log("\n--- PHASE 1: Happy Path Order Processing Workflow & Replay Engine ---");
        Console.ResetColor();

        var happyOrderId = "ord-1001";
        var happyInstanceId = new WorkflowInstanceId($"wf-order-{happyOrderId}");
        var happyRequest = new OrderProcessingRequest(
            happyOrderId,
            "Alice Developer",
            "SKU-DEV-WORKSTATION",
            1,
            899.99m);

        Log($"Starting workflow '{nameof(OrderProcessingWorkflow)}' (Instance: {happyInstanceId.Value})...");
        await client.StartWorkflowAsync<OrderProcessingWorkflow, OrderProcessingRequest>(
            happyRequest,
            happyInstanceId.Value,
            cancellationToken).ConfigureAwait(false);

        Log($"Awaiting completion of {happyInstanceId.Value} (includes 50ms durable timer delay)...");
        var happyResult = await client.WaitForWorkflowCompletionAsync<OrderProcessingResult>(
            happyInstanceId,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        Log($"Order Result: Status={happyResult?.Status}, TxnId={happyResult?.TransactionId}, Tracking={happyResult?.TrackingNumber}");
        var happyHistory = await client.GetWorkflowHistoryAsync(happyInstanceId, cancellationToken).ConfigureAwait(false);
        Log($"Workflow event stream ({happyHistory.Count} events recorded):");
        foreach (var evt in happyHistory)
        {
            Log($"   • [Event #{evt.EventId}] {evt.EventType} - {evt.Name} (Timestamp: {evt.Timestamp:HH:mm:ss.fff})");
        }

        var happyPathCompleted = happyResult?.Status == "Completed" && !string.IsNullOrWhiteSpace(happyResult.TrackingNumber);

        // --- PHASE 2: DISTRIBUTED SAGA & LIFO COMPENSATION ROLLBACK ---
        Console.ForegroundColor = ConsoleColor.Yellow;
        Log("\n--- PHASE 2: Distributed Saga & Automated LIFO Compensation Rollback ---");
        Console.ResetColor();

        ReleaseInventoryCompensationActivity.CompensationExecutions = 0;
        var sagaOrderId = "ord-1002";
        var sagaInstanceId = new WorkflowInstanceId($"wf-order-{sagaOrderId}");
        var sagaRequest = new OrderProcessingRequest(
            sagaOrderId,
            "Bob Architect",
            "SKU-ENTERPRISE-GPU-RACK",
            2,
            2499.00m); // Exceeds $1,000 threshold in ProcessPaymentActivity!

        Log($"Starting saga workflow with amount ${sagaRequest.TotalAmount:F2} (> $1,000 credit limit)...");
        await client.StartWorkflowAsync<OrderProcessingWorkflow, OrderProcessingRequest>(
            sagaRequest,
            sagaInstanceId.Value,
            cancellationToken).ConfigureAwait(false);

        var sagaResult = await client.WaitForWorkflowCompletionAsync<OrderProcessingResult>(
            sagaInstanceId,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        Log($"Saga Result: Status={sagaResult?.Status}, Notes={sagaResult?.Notes}");
        Log($"Compensation Activity Executions: {ReleaseInventoryCompensationActivity.CompensationExecutions}");

        var sagaHistory = await client.GetWorkflowHistoryAsync(sagaInstanceId, cancellationToken).ConfigureAwait(false);
        Log($"Saga event stream ({sagaHistory.Count} events recorded):");
        foreach (var evt in sagaHistory)
        {
            Log($"   • [Event #{evt.EventId}] {evt.EventType} - {evt.Name} {evt.Details}");
        }

        var sagaRollbackSucceeded = sagaResult?.Status == "Failed" && ReleaseInventoryCompensationActivity.CompensationExecutions == 1;

        // --- PHASE 3: CLOUDEVENTS EXTERNAL EVENT AWAITS (HUMAN-IN-THE-LOOP) ---
        Console.ForegroundColor = ConsoleColor.Magenta;
        Log("\n--- PHASE 3: Long-Running Workflow & CloudEvents External Event Await ---");
        Console.ResetColor();

        var approvalRequestId = "req-approv-801";
        var approvalInstanceId = new WorkflowInstanceId($"wf-approval-{approvalRequestId}");
        var approvalRequest = new ApprovalRequest(
            approvalRequestId,
            "DevOps Infrastructure Team",
            7500.00m,
            "Production Cloud Cluster Capacity Upgrade");

        Log($"Starting approval workflow {approvalInstanceId.Value}...");
        await client.StartWorkflowAsync<ManagerApprovalWorkflow, ApprovalRequest>(
            approvalRequest,
            approvalInstanceId.Value,
            cancellationToken).ConfigureAwait(false);

        // Verify suspended state
        var suspendedState = await client.GetWorkflowStateAsync(approvalInstanceId, cancellationToken).ConfigureAwait(false);
        Log($"Workflow state after start: Status={suspendedState?.Status}, CustomStatus={suspendedState?.CustomStatus}");

        Log("Simulating human-in-the-loop decision via external event 'ManagerDecision'...");
        var decision = new ApprovalResponse("director-jane-doe", true, "Budget approved under Q3 infra authorization.");
        await client.RaiseEventAsync(approvalInstanceId, ManagerApprovalWorkflow.DecisionEventName, decision, cancellationToken)
            .ConfigureAwait(false);

        var approvalResult = await client.WaitForWorkflowCompletionAsync<ApprovalResponse>(
            approvalInstanceId,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        Log($"Approval Workflow Finished: Approver={approvalResult?.ApproverId}, Approved={approvalResult?.Approved}, Comments='{approvalResult?.Comments}'");
        var externalApprovalSucceeded = approvalResult?.Approved == true;

        stopwatch.Stop();

        // --- SUMMARY REPORT ---
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n================================================================================");
        Console.WriteLine(" CENTRA WORKFLOW SIMULATION RESULTS SUMMARY                                     ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        Log($"1. Happy Path Workflow (Replay + Timer):   [{(happyPathCompleted ? "PASS" : "FAIL")}] (Order: {happyOrderId})");
        Log($"2. Saga LIFO Compensation Rollback:       [{(sagaRollbackSucceeded ? "PASS" : "FAIL")}] (Compensated: {ReleaseInventoryCompensationActivity.CompensationExecutions})");
        Log($"3. CloudEvents External Event Await:      [{(externalApprovalSucceeded ? "PASS" : "FAIL")}] (Approver: {approvalResult?.ApproverId})");
        Log($"Total simulation duration: {stopwatch.ElapsedMilliseconds} ms");

        await host.StopAsync(cancellationToken).ConfigureAwait(false);

        return new WorkflowSimulationResult(
            happyPathCompleted,
            sagaRollbackSucceeded,
            externalApprovalSucceeded,
            happyOrderId,
            sagaOrderId,
            approvalRequestId,
            logs);
    }
}
