using Centra.Bindings;
using Centra.Sample.Bindings.Services;
using Centra.State;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.Bindings.Jobs;

/// <summary>
/// A scheduled domain job that captures periodic inventory snapshots.
/// <para>
/// <b>Centra Bindings Features Demonstrated:</b>
/// <list type="bullet">
///   <item><b>Declarative Scheduling</b>: Decorated with <see cref="CronBindingAttribute"/> for automatic scanning.</item>
///   <item><b>Standard Cron Syntax</b>: Uses 6-field cron (<c>*/2 * * * * *</c>) to execute every 2 seconds on the even second.</item>
///   <item><b>Cluster-Wide Single-Execution</b>: In a multi-node cluster, Centra automatically coordinates via distributed
///   locks (<c>cron:{jobName}:{unixTimestamp}</c>) so that <b>only ONE node</b> executes each tick across all running replicas.</item>
///   <item><b>ScheduledJobContext</b>: Receives rich contextual metadata including scheduled vs actual time, iteration, and cancellation token.</item>
/// </list>
/// </para>
/// </summary>
public sealed class InventorySnapshotJob : IJobHandler
{
    private readonly IStateStore<InventorySnapshot> _stateStore;
    private readonly INodeContext _nodeContext;
    private readonly ClusterExecutionTracker _tracker;
    private readonly ILogger<InventorySnapshotJob> _logger;

    public InventorySnapshotJob(
        IStateStore<InventorySnapshot> stateStore,
        INodeContext nodeContext,
        ClusterExecutionTracker tracker,
        ILogger<InventorySnapshotJob> logger)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _nodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Invoked by Centra's scheduler when the cron expression triggers.
    /// The <see cref="CronBindingAttribute"/> accepts standard 5-part cron, 6-part cron with seconds, or descriptors.
    /// </summary>
    /// <param name="context">
    /// The scheduled job context containing:
    /// <list type="bullet">
    ///   <item><see cref="ScheduledJobContext.JobName"/>: "InventorySnapshotJob"</item>
    ///   <item><see cref="ScheduledJobContext.ScheduledTime"/>: The precise target time calculated from the cron schedule</item>
    ///   <item><see cref="ScheduledJobContext.ActualTime"/>: The actual time this node began execution</item>
    ///   <item><see cref="ScheduledJobContext.Iteration"/>: Monotonically increasing tick counter</item>
    ///   <item><see cref="ScheduledJobContext.CancellationToken"/>: Triggered when application shuts down</item>
    /// </list>
    /// </param>
    [CronBinding("*/2 * * * * *")]
    public async ValueTask ExecuteAsync(ScheduledJobContext context)
    {
        var nodeId = _nodeContext.NodeId;

        // 1. Record execution in the cluster tracker for demo metrics
        _tracker.RecordExecution(nodeId, context.Iteration, context.ScheduledTime, context.ActualTime);

        // 2. Perform domain work: persist the inventory snapshot in the shared distributed state store
        var snapshot = new InventorySnapshot(
            Timestamp: context.ScheduledTime,
            TotalItems: 1500,
            Iteration: context.Iteration,
            ExecutedByNodeId: nodeId);

        await _stateStore.SetAsync("current-snapshot", snapshot, cancellationToken: context.CancellationToken)
            .ConfigureAwait(false);

        // 3. Output visual indicator showing this node acquired the lock and executed
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"   👑 [{nodeId}] WON LOCK -> Executed tick #{context.Iteration} at {context.ScheduledTime:HH:mm:ss.fff} (Snapshot saved to state store)");
        Console.ResetColor();

        _logger.LogInformation(
            "[{NodeId}] Executed cron job '{JobName}' tick #{Iteration} at {ScheduledTime:HH:mm:ss.fff}.",
            nodeId,
            context.JobName,
            context.Iteration,
            context.ScheduledTime);

        // 4. Brief async simulated work: simulates database access or external API call.
        // Holding the execution turn briefly ensures that peer nodes attempting the same tick experience lock contention.
        await Task.Delay(100, context.CancellationToken).ConfigureAwait(false);
    }
}
