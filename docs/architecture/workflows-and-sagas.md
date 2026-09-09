# Distributed Workflows & Sagas Engine

> How Centra achieves crash-resilient multi-step business orchestrations via deterministic event-sourced replay, durable timers, and automated reverse LIFO saga compensations.

---

## 🔄 1. Deterministic Replay Architecture

Centra Workflows orchestrate business logic across long periods of time (seconds to days). Instead of keeping worker threads blocked in memory, workflows execute as **Deterministic State Machines** backed by an append-only event stream (`IWorkflowHistoryStore`):

```
                   Step 1: Activity A completes (Output recorded in History)
                   Step 2: Workflow suspends waiting for Timer / CloudEvent
                                       │
                         [Process restarts or wakes up]
                                       │
                                       ▼
                   Workflow Replays from Beginning:
                   - Context matches past Activity A execution in History
                   - Context SKIPS live execution of Activity A
                   - Context immediately returns cached Output of Activity A
                   - Context resumes live execution at Step 3!
```

### Determinism Invariants
Because the orchestrator function (`Workflow<TInput, TOutput>.RunAsync`) is re-executed from scratch during replay, it **must be 100% deterministic**:
1. **No Direct System Clock**: Never call `DateTime.UtcNow`. Use `context.CurrentUtcDateTime`, which returns the recorded timestamp during replays.
2. **No Non-Deterministic Guids**: Never call `Guid.NewGuid()`. Use `context.NewGuid()`, which generates deterministic seeded Guids based on history turn count.
3. **No Unmanaged Side Effects in Orchestrator**: HTTP calls, database updates, or random number generation must occur inside `WorkflowActivity<TIn, TOut>` steps, never directly in the orchestrator method.
4. **Replay Flag**: Check `context.IsReplaying` if custom diagnostic branching is needed.

---

## 🛡️ 2. Distributed Sagas & Automated LIFO Compensation

In distributed architectures, traditional two-phase commit (2PC) transactions across heterogeneous services degrade availability. Centra implements the **Saga Pattern**:

```
Forward Execution:
[Step 1: Reserve Inventory] ──> [Step 2: Charge Payment] ──> [Step 3: Ship Order]
         │                                │
  Registers Compensation          Registers Compensation
         │                                │
         ▼                                ▼
[Comp 1: Release Inventory]      [Comp 2: Refund Payment]

════════════════════════════════════════════════════════════════════════════════
Failure Occurs at Step 3!
Automated LIFO Reverse Rollback:
[Compensate Step 2: Refund Payment] ──> [Compensate Step 1: Release Inventory]
```

### Saga API Usage
Within the workflow orchestrator:

```csharp
var saga = context.CreateSaga();

// Step 1: Call activity
var reservation = await context.CallActivityAsync<ReserveInventoryActivity, OrderRequest, Reservation>(input);

// Register corresponding compensation step
saga.AddCompensation<ReleaseInventoryCompensationActivity, Reservation>(reservation);

try
{
    // Step 2: Call payment
    var payment = await context.CallActivityAsync<ChargePaymentActivity, OrderRequest, PaymentResult>(input);
    saga.AddCompensation<RefundPaymentCompensationActivity, PaymentResult>(payment);

    // Step 3: Call shipping (may throw exception)
    var shipment = await context.CallActivityAsync<ShipOrderActivity, OrderRequest, Shipment>(input);
    return new OrderResult("Success", shipment.TrackingNumber);
}
catch (WorkflowSuspendedException)
{
    throw; // Preserve suspension propagation for timers/events
}
catch (Exception ex)
{
    // Execute all registered compensations in strict reverse (LIFO) order
    await saga.CompensateAsync();
    return new OrderResult("Failed", $"Compensated: {ex.Message}");
}
```

---

## ⏱️ 3. Durable Timers

When an orchestration needs to wait (e.g. 30 seconds for verification, or 3 days for reminder escalation), calling `Task.Delay` would waste memory and fail if the server restarts.

With Centra durable timers:
```csharp
await context.CreateTimerAsync(TimeSpan.FromMinutes(10));
```
1. `CreateTimerAsync` records a `TimerCreated` event in the history stream with the target expiration timestamp.
2. The engine throws a `WorkflowSuspendedException`, cleanly terminating the current turn and saving workflow state to `IWorkflowHistoryStore`.
3. A background timer scheduler wakes the workflow up when the deadline arrives.
4. On wake-up, the workflow replays to that point and continues to the next statement.

---

## 📨 4. CloudEvents External Event Awaits

Workflows can pause execution waiting for human approval or an external domain event:

```csharp
var approval = await context.WaitForExternalEventAsync<ManagerApprovalResponse>("ManagerApproval");
```
1. Workflow transitions to `WorkflowStatus.Suspended`.
2. When the manager clicks "Approve", the UI or webhook calls:
   ```csharp
   await workflowClient.RaiseEventAsync(instanceId, "ManagerApproval", new ManagerApprovalResponse(Approved: true));
   ```
3. The event payload is appended to the workflow history and execution resumes automatically.
