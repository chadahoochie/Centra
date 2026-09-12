using System.Collections.Concurrent;
using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IDurableWorkflowTimerStore"/> for testing and local dev.
/// </summary>
public sealed class InMemoryDurableWorkflowTimerStore : IDurableWorkflowTimerStore
{
    private readonly ConcurrentDictionary<WorkflowInstanceId, DurableWorkflowTimerRecord> _timers = new();

    public int Count => _timers.Count;

    public ValueTask SaveTimerAsync(DurableWorkflowTimerRecord timer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timer);
        _timers[timer.InstanceId] = timer;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<DurableWorkflowTimerRecord>> GetDueTimersAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken = default)
    {
        var due = _timers.Values
            .Where(t => t.DueTimeUtc <= asOfUtc)
            .OrderBy(t => t.DueTimeUtc)
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<DurableWorkflowTimerRecord>>(due);
    }

    public ValueTask DeleteTimerAsync(WorkflowInstanceId instanceId, CancellationToken cancellationToken = default)
    {
        _timers.TryRemove(instanceId, out _);
        return ValueTask.CompletedTask;
    }
}
