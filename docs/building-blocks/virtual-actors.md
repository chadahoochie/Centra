# Distributed Virtual Actors Runtime

> High-throughput virtual actors with turn-based sequential single-threaded execution, dirty-tracked state commits, dynamic RPC proxy generation, ephemeral timers, and durable reminders.

---

## 🎯 Key Concepts

- **Virtual Presence**: Actors exist conceptually forever. They are activated in memory on their first invocation and passivated automatically when idle.
- **Turn-Based Concurrency**: An actor never processes two calls concurrently. All operations enter the `ActorMailbox` and execute sequentially.
- **Dirty-Tracking State**: Mutations staged in `IActorStateManager` are committed atomically at the end of each turn via ETag CAS.
- **Consistent Hash Partition Placement**: Maps actors across cluster replicas with 100 vnodes per replica.

---

## 🛠️ Step 1: Define Actor Interface

The actor interface must inherit from `IActor`. If it receives reminders, it also implements `IRemindable`:

```csharp
using Centra.Actors;

namespace MyDomain.Actors;

public interface IAccountActor : IActor, IRemindable
{
    ValueTask<decimal> GetBalanceAsync();
    ValueTask<decimal> DepositAsync(decimal amount);
    ValueTask<bool> WithdrawAsync(decimal amount);
    ValueTask ScheduleInterestReminderAsync(TimeSpan period);
}
```

---

## 🏗️ Step 2: Implement the Actor

Inherit from the base class `Actor` and implement your interface:

```csharp
using Centra.Actors;

namespace MyDomain.Actors;

[Actor("AccountActor")]
public sealed class AccountActor : Actor, IAccountActor
{
    public async ValueTask<decimal> GetBalanceAsync()
    {
        var balance = await StateManager.GetStateAsync<decimal>("balance");
        return balance;
    }

    public async ValueTask<decimal> DepositAsync(decimal amount)
    {
        var balance = await StateManager.GetStateAsync<decimal>("balance");
        var updated = balance + amount;
        
        // Stage state update in StateManager
        await StateManager.SetStateAsync("balance", updated);

        // State is automatically committed via ETag at the end of this turn!
        return updated;
    }

    public async ValueTask<bool> WithdrawAsync(decimal amount)
    {
        var balance = await StateManager.GetStateAsync<decimal>("balance");
        if (balance < amount)
        {
            return false;
        }

        await StateManager.SetStateAsync("balance", balance - amount);
        return true;
    }

    public async ValueTask ScheduleInterestReminderAsync(TimeSpan period)
    {
        // Register a reminder that survives node restarts and passivation
        await ReminderManager.RegisterReminderAsync(
            reminderName: "interest-accrual",
            state: ReadOnlyMemory<byte>.Empty,
            dueTime: period,
            period: period);
    }

    // Called periodically by ActorReminderCoordinator with distributed lock coordination
    public async ValueTask ReceiveReminderAsync(
        string reminderName, 
        ReadOnlyMemory<byte> state, 
        TimeSpan dueTime, 
        TimeSpan period, 
        CancellationToken cancellationToken)
    {
        if (reminderName == "interest-accrual")
        {
            var balance = await StateManager.GetStateAsync<decimal>("balance");
            var interest = balance * 0.05m;
            await StateManager.SetStateAsync("balance", balance + interest);
        }
    }
}
```

---

## ⚙️ Step 3: Registration in `Program.cs`

Centra provides two ways to register virtual actors:

### Option A: Declarative via `[Actor]` Attribute (Automatic Discovery)
Decorate your actor implementation with `[Actor("AccountActor")]` and invoke `builder.Services.AddCentra()`:

```csharp
// Registers actor runtime and automatically scans assembly for [Actor] classes
builder.Services.AddCentra(options =>
{
    options.DefaultStateStore = "statestore";
});

var app = builder.Build();
app.MapCentraActorEndpoints(); // Mounts inter-node actor invocation routes
```

> [!NOTE]
> For automatic discovery, your actor class must implement **exactly one** domain interface derived from `IActor` (e.g. `IAccountActor`). If an actor implements multiple `IActor` interfaces, register it explicitly using Option B.

### Option B: Programmatic via `AddCentraActor` Extension (Explicit Modular Registration)
When registering actor building blocks modularly without the umbrella metapackage, or when disambiguating actor interfaces, register explicitly:

```csharp
// 1. Add actor runtime only (modular registration adhering to ISP)
builder.Services.AddCentraActors(options =>
{
    options.ActorIdleTimeout = TimeSpan.FromMinutes(15);
    options.DefaultStateStore = "statestore";
});

// 2. Explicitly register actor implementation and interface
builder.Services.AddCentraActor<AccountActor, IAccountActor>();

var app = builder.Build();
app.MapCentraActorEndpoints();
```

---

## 🚀 Step 4: Client Invocation via `IActorProxyFactory`

Inject `IActorProxyFactory` into controllers, minimal APIs, or services:

```csharp
app.MapPost("/accounts/{id}/deposit", async (
    string id, 
    decimal amount, 
    IActorProxyFactory proxyFactory) =>
{
    // Create strongly typed client proxy
    var account = proxyFactory.CreateActorProxy<IAccountActor>("AccountActor", id);

    // Call runs sequentially on owning node
    var newBalance = await account.DepositAsync(amount);

    return Results.Ok(new { balance = newBalance });
});
```

---

## ⏰ Ephemeral Timers vs Durable Reminders

| Feature | Ephemeral Timers (`ActorTimerManager`) | Durable Reminders (`IActorReminderManager`) |
| :--- | :--- | :--- |
| **Storage** | In-memory only | Persisted to `IStateStore` |
| **Passivation** | Cancels when actor is passivated | Wakes up actor from sleep / cold state |
| **Node Failover** | Lost on process restart | Re-scheduled on cluster peers |
| **Coordination** | Local to process | Distributed lock mutual exclusion |
| **Typical Use** | Fast UI ticks, flush buffers | Monthly billing, interest calculation, SLAs |
