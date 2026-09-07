using Centra.Bindings;
using Centra.State;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.Bindings.Jobs;

public sealed class InventorySnapshotJob : IJobHandler
{
    private readonly IStateStore<InventorySnapshot> _stateStore;
    private readonly ILogger<InventorySnapshotJob> _logger;

    public static long ExecutionCount;

    public InventorySnapshotJob(
        IStateStore<InventorySnapshot> stateStore,
        ILogger<InventorySnapshotJob> logger)
    {
        _stateStore = stateStore;
        _logger = logger;
    }

    public async ValueTask ExecuteAsync(ScheduledJobContext context)
    {
        Interlocked.Increment(ref ExecutionCount);

        var snapshot = new InventorySnapshot(
            Timestamp: context.ScheduledTime,
            TotalItems: 1500,
            Iteration: context.Iteration);

        await _stateStore.SetAsync("current-snapshot", snapshot, cancellationToken: context.CancellationToken);

        _logger.LogInformation(
            "[CRON JOB] '{JobName}' executed iteration #{Iteration} at {ScheduledTime:HH:mm:ss.fff}. Snapshot persisted.",
            context.JobName,
            context.Iteration,
            context.ScheduledTime);
    }
}
