using System.Collections.Concurrent;

namespace Centra.Sample.Bindings.Services;

/// <summary>
/// Thread-safe coordinator tracker shared across all simulated application nodes.
/// Records each tick execution (the winning node) and each tick skip (nodes that backed off).
/// </summary>
public sealed class ClusterExecutionTracker
{
    private readonly ConcurrentBag<CronExecutionRecord> _executions = new();
    private readonly ConcurrentDictionary<string, int> _executionsPerNode = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _skipsPerNode = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<DateTimeOffset> _ticksObserved = new();

    public void RecordExecution(string nodeId, long iteration, DateTimeOffset scheduledTime, DateTimeOffset executedTime)
    {
        _executions.Add(new CronExecutionRecord(nodeId, iteration, scheduledTime, executedTime));
        _executionsPerNode.AddOrUpdate(nodeId, 1, static (_, count) => count + 1);
        _ticksObserved.Add(scheduledTime);
    }

    public void RecordSkip(string nodeId)
    {
        _skipsPerNode.AddOrUpdate(nodeId, 1, static (_, count) => count + 1);
    }

    public IReadOnlyCollection<CronExecutionRecord> Executions => _executions;
    public IReadOnlyDictionary<string, int> ExecutionsPerNode => _executionsPerNode;
    public IReadOnlyDictionary<string, int> SkipsPerNode => _skipsPerNode;
    public int TotalExecutions => _executions.Count;
    public int TotalSkips => _skipsPerNode.Values.Sum();
    public int UniqueTicksCount => _ticksObserved.Distinct().Count();
}
