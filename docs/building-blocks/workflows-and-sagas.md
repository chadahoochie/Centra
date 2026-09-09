# Distributed Workflows & Sagas Developer Guide

> Author deterministic business orchestrations, automated LIFO saga compensations, durable timers, and CloudEvents human-in-the-loop external event awaits.

---

## 🎯 Core Building Blocks

- **`Workflow<TInput, TOutput>`**: Base class for deterministic workflow orchestrations.
- **`WorkflowActivity<TInput, TOutput>`**: Base class for discrete, retryable activity steps.
- **`IWorkflowContext`**: Replay-aware orchestration context (`CallActivityAsync`, `CreateTimerAsync`, `WaitForExternalEventAsync`, `CreateSaga`).
- **`IWorkflowClient`**: API for starting, querying, raising events to, and terminating workflows.

---

## 🛠️ Step 1: Define Activities

Activities contain external side effects (database writes, credit card charges, HTTP calls). Every activity execution is tracked in the event history and wrapped in Polly v8 resilience pipelines:

```csharp
using Centra.Workflows;

// 1. Forward Activity: Reserve Inventory
[WorkflowActivity("ReserveInventory")]
public sealed class ReserveInventoryActivity : WorkflowActivity<OrderRequest, InventoryReservation>
{
    public override async ValueTask<InventoryReservation> RunAsync(
        WorkflowActivityContext context, 
        OrderRequest input)
    {
        // Live inventory call
        var reservationId = $"res-{Guid.NewGuid():N}"[..8];
        return new InventoryReservation(reservationId, input.ProductId, input.Quantity);
    }
}

// 2. Compensating Activity: Release Inventory on Failure
[WorkflowActivity("ReleaseInventoryCompensation")]
public sealed class ReleaseInventoryCompensationActivity : WorkflowActivity<InventoryReservation, bool>
{
    public override async ValueTask<bool> RunAsync(
        WorkflowActivityContext context, 
        InventoryReservation input)
    {
        // Compensate by releasing reserved items
        return true;
    }
}

// 3. Process Payment Activity (may throw on insufficient funds)
[WorkflowActivity("ProcessPayment")]
public sealed class ProcessPaymentActivity : WorkflowActivity<OrderRequest, string>
{
    public override async ValueTask<string> RunAsync(
        WorkflowActivityContext context, 
        OrderRequest input)
    {
        if (input.TotalAmount > 1000.00m)
        {
            throw new InvalidOperationException("Payment declined: credit limit exceeded.");
        }

        return $"txn-{Guid.NewGuid():N}"[..12];
    }
}
```

---

## 🔄 Step 2: Author the Workflow Orchestrator

Inherit from `Workflow<TInput, TOutput>` and implement `RunAsync`:

```csharp
using Centra.Workflows;

[Workflow("OrderProcessingWorkflow")]
public sealed class OrderProcessingWorkflow : Workflow<OrderRequest, OrderResult>
{
    public override async ValueTask<OrderResult> RunAsync(IWorkflowContext context, OrderRequest input)
    {
        var saga = context.CreateSaga();

        // 1. Call ReserveInventoryActivity
        var reservation = await context.CallActivityAsync<ReserveInventoryActivity, OrderRequest, InventoryReservation>(input);

        // 2. Register compensation in the saga
        saga.AddCompensation<ReleaseInventoryCompensationActivity, InventoryReservation>(reservation);

        try
        {
            // 3. Charge payment (will fail if input.TotalAmount > 1000)
            var txnId = await context.CallActivityAsync<ProcessPaymentActivity, OrderRequest, string>(input);

            // 4. Durable timer: pause workflow for 30 seconds without thread blocking
            await context.CreateTimerAsync(TimeSpan.FromSeconds(30));

            // 5. Human-in-the-loop: await external CloudEvent approval if high-value order
            if (input.TotalAmount > 500.00m)
            {
                var approval = await context.WaitForExternalEventAsync<ApprovalEvent>("ManagerApproval");
                if (!approval.Approved)
                {
                    throw new InvalidOperationException($"Rejected by manager: {approval.Reason}");
                }
            }

            return new OrderResult(input.OrderId, "Completed", txnId, "Order processed successfully.");
        }
        catch (WorkflowSuspendedException)
        {
            throw; // Must re-throw to allow timer/event suspension to bubble up cleanly
        }
        catch (Exception ex)
        {
            // 6. Automatically roll back completed activities in reverse order (LIFO)
            await saga.CompensateAsync();

            return new OrderResult(input.OrderId, "Failed", null, $"Compensated due to error: {ex.Message}");
        }
    }
}
```

---

## ⚙️ Step 3: Registration in `Program.cs`

Centra provides two ways to register workflows and activities:

### Option A: Declarative via `[Workflow]` and `[WorkflowActivity]` Attributes (Automatic Discovery)
Decorate your workflow classes with `[Workflow]` and activity classes with `[WorkflowActivity]`, then invoke `builder.Services.AddCentra()`:

```csharp
// Registers workflow engine and automatically scans assembly for [Workflow] and [WorkflowActivity]
builder.Services.AddCentra(options =>
{
    options.DefaultStateStore = "statestore";
});

var app = builder.Build();
app.MapCentraWorkflowEndpoints(); // Mounts workflow HTTP endpoints
```

Centra's reflection scanner discovers all decorated classes, extracts their generic input and output types, and registers them into the `IWorkflowRegistry`.

### Option B: Programmatic via Service Collection Extensions (Explicit Modular Registration)
When registering workflow engine building blocks modularly without the full framework umbrella, register them explicitly:

```csharp
// 1. Add workflow engine only (modular registration adhering to ISP)
builder.Services.AddCentraWorkflows(options =>
{
    options.DefaultStateStore = "statestore";
});

// 2. Explicitly register workflows and activities
builder.Services.AddCentraWorkflow<OrderProcessingWorkflow>();
builder.Services.AddCentraWorkflowActivity<ReserveInventoryActivity>();
builder.Services.AddCentraWorkflowActivity<ReleaseInventoryCompensationActivity>();
builder.Services.AddCentraWorkflowActivity<ProcessPaymentActivity>();

var app = builder.Build();
app.MapCentraWorkflowEndpoints();
```

---

## 🚀 Step 4: Client Operations (`IWorkflowClient`)

Start, query, or interact with workflows:

```csharp
app.MapPost("/workflows/orders", async (OrderRequest request, IWorkflowClient client) =>
{
    var instanceId = await client.StartWorkflowAsync<OrderRequest, OrderResult>(
        workflowName: "OrderProcessingWorkflow",
        input: request);

    return Results.Accepted($"/workflows/orders/{instanceId.Value}", new { instanceId = instanceId.Value });
});

app.MapPost("/workflows/orders/{instanceId}/approve", async (string instanceId, IWorkflowClient client) =>
{
    // Resume suspended workflow awaiting external event
    await client.RaiseEventAsync(
        new WorkflowInstanceId(instanceId), 
        eventName: "ManagerApproval", 
        eventData: new ApprovalEvent(Approved: true, Reason: "Approved by Admin"));

    return Results.Ok();
});

app.MapGet("/workflows/orders/{instanceId}", async (string instanceId, IWorkflowClient client) =>
{
    var state = await client.GetWorkflowStateAsync(new WorkflowInstanceId(instanceId));
    return state is not null ? Results.Ok(state) : Results.NotFound();
});
```
