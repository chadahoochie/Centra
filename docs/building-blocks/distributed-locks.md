# Distributed Mutual Exclusion & Locks

> Acquire and manage non-blocking, crash-safe distributed locks with automatic heartbeat lease renewal and leader election across cluster replicas.

---

## 🎯 Key Interfaces

- **`IDistributedLockProvider`**: Client for requesting and releasing distributed mutual exclusion locks.
- **`IDistributedLock`**: Represents an acquired lease implementing `IAsyncDisposable`.

---

## 🛠️ Registration

In `Program.cs`, you can register distributed locks using the fluent config builder or modular service collection extensions:

```csharp
// Option A: Fluent Builder via ICentraBuilder
builder.Services.AddCentra()
    .ConfigureLocks(options =>
    {
        options.DefaultStore = "lockstore";
        options.DefaultExpiry = TimeSpan.FromSeconds(30);
        options.DefaultTimeout = TimeSpan.FromSeconds(5);
    })
    .AddRedis(options => options.Configuration = "localhost:6379");

// Option B: Modular Service Collection Registration (ISP)
builder.Services.AddCentraLocks();
builder.Services.AddCentraRedis(options => options.Configuration = "localhost:6379");
```

---

## 📖 Acquiring a Lock

Inject `IDistributedLockProvider` into endpoints, services, or background tasks:

```csharp
app.MapPost("/inventory/deduct", async (
    DeductStockRequest request, 
    IDistributedLockProvider lockProvider) =>
{
    // Acquire distributed lock on product ID
    await using var @lock = await lockProvider.AcquireLockAsync(
        storeName: "lockstore",
        resourceId: $"product:{request.ProductId}",
        expiryTime: TimeSpan.FromSeconds(30),  // Lease duration
        timeout: TimeSpan.FromSeconds(5));     // Max wait time to acquire

    if (!@lock.Success)
    {
        // Another replica or thread holds the lock
        return Results.Conflict("Could not acquire lock for inventory modification.");
    }

    // Critical section begins
    await DeductStockInternalAsync(request.ProductId, request.Quantity);

    return Results.Ok();
    // Disposal automatically releases the lock lease atomically
});
```

---

## 🔄 Automatic Background Lease Renewal

When processing a long-running batch job where the duration may exceed the initial `expiryTime`, Centra prevents lock loss:
- The `IDistributedLock` acquisition launches a background heartbeat timer.
- It refreshes the lease in storage at `expiryTime / 3` intervals.
- The lock cannot be stolen by another node while the holding process remains alive.
- When `await @lock.DisposeAsync()` is reached, the heartbeat cancels immediately and the lock is released.

---

## 👑 Leader Election Pattern

To ensure only one node in a cluster runs a master coordinator loop:

```csharp
public sealed class ClusterLeaderWorker : BackgroundService
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<ClusterLeaderWorker> _logger;

    public ClusterLeaderWorker(
        IDistributedLockProvider lockProvider, 
        ILogger<ClusterLeaderWorker> logger)
    {
        _lockProvider = lockProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Attempt to acquire leader lease for 20 seconds
                await using var leaderLock = await _lockProvider.AcquireLockAsync(
                    storeName: "lockstore",
                    resourceId: "cluster-leader",
                    expiryTime: TimeSpan.FromSeconds(20),
                    timeout: TimeSpan.FromSeconds(2),
                    cancellationToken: stoppingToken);

                if (leaderLock.Success)
                {
                    _logger.LogInformation("Promoted to cluster leader. Running primary loop...");
                    
                    // Stay in leader loop while lease is automatically renewed
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await RunLeaderDutiesAsync(stoppingToken);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
                else
                {
                    // Standby node: wait before next election attempt
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Leader loop failed; yielding leadership.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
```
