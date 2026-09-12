using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Serialization;
using Centra.Workflows;
using Microsoft.Extensions.Logging;

namespace Centra.Core.Workflows;

/// <summary>
/// Core execution engine coordinating workflow lifecycles, deterministic replay, state, and sagas.
/// </summary>
public sealed class WorkflowEngine : IWorkflowEngine, IDisposable
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowHistoryStore _historyStore;
    private readonly ICentraSerializer _serializer;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowTimerScheduler _timerScheduler;
    private readonly IWorkflowTurnProcessor _turnProcessor;
    private readonly IDurableWorkflowTimerStore? _durableTimerStore;

    public WorkflowEngine(
        IServiceProvider serviceProvider,
        IWorkflowRegistry registry,
        IWorkflowHistoryStore historyStore,
        IWorkflowActivityDispatcher dispatcher,
        ICentraSerializer serializer,
        TimeProvider? timeProvider = null,
        ILogger<WorkflowEngine>? logger = null,
        IWorkflowRunnerInvoker? runnerInvoker = null,
        IWorkflowTimerScheduler? timerScheduler = null,
        IWorkflowTurnProcessor? turnProcessor = null,
        IDurableWorkflowTimerStore? durableTimerStore = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _durableTimerStore = durableTimerStore;

        _timerScheduler = timerScheduler ?? new WorkflowTimerScheduler(_timeProvider, logger);
        var invoker = runnerInvoker ?? WorkflowRunnerInvoker.Instance;
        _turnProcessor = turnProcessor ?? new WorkflowTurnProcessor(
            serviceProvider,
            historyStore,
            dispatcher,
            serializer,
            invoker,
            _timerScheduler,
            _timeProvider,
            async id => await FireTimerAsync(id).ConfigureAwait(false),
            _durableTimerStore,
            logger);
    }

    public async ValueTask<WorkflowInstanceId> StartWorkflowAsync(
        string workflowName,
        object? input,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);

        if (!_registry.TryGetWorkflow(workflowName, out var def))
        {
            throw new InvalidOperationException($"Workflow '{workflowName}' is not registered in the workflow registry.");
        }

        var actualId = string.IsNullOrWhiteSpace(instanceId)
            ? WorkflowInstanceId.New()
            : new WorkflowInstanceId(instanceId);

        var now = _timeProvider.GetUtcNow();
        byte[] inputBytes = [];
        if (input is not null)
        {
            inputBytes = input is byte[] b ? b : _serializer.Serialize(input);
        }

        var startedEvent = new WorkflowHistoryEventRecord
        {
            EventId = 1,
            EventType = (int)WorkflowHistoryEventType.WorkflowStarted,
            Name = workflowName,
            Timestamp = now,
            Data = inputBytes
        };

        var stateRecord = new WorkflowStateRecord
        {
            InstanceId = actualId.Value,
            WorkflowName = workflowName,
            Status = (int)WorkflowStatus.Running,
            Input = inputBytes,
            CreatedAt = now,
            LastUpdatedAt = now
        };

        await _historyStore.SaveStateAsync(stateRecord, cancellationToken).ConfigureAwait(false);
        await _historyStore.AppendHistoryAsync(actualId, [startedEvent], cancellationToken).ConfigureAwait(false);

        using var activity = CentraDiagnostics.Source.StartActivity("Centra.Workflow.Start", ActivityKind.Internal);
        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.DisplayName = $"Workflow {workflowName}";
            activity.SetTag("centra.component", "workflow");
            activity.SetTag("centra.workflow.instance_id", actualId.Value);
            activity.SetTag("centra.workflow.name", workflowName);
        }

        await _turnProcessor.ProcessTurnAsync(actualId, def, stateRecord, cancellationToken).ConfigureAwait(false);
        return actualId;
    }

    public async ValueTask<WorkflowState?> GetWorkflowStateAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        var record = await _historyStore.GetStateAsync(instanceId, cancellationToken).ConfigureAwait(false);
        if (record is null) return null;

        return new WorkflowState(
            record.InstanceId,
            record.WorkflowName,
            (WorkflowStatus)record.Status,
            record.Input ?? ReadOnlyMemory<byte>.Empty,
            record.Output ?? ReadOnlyMemory<byte>.Empty,
            record.CustomStatus,
            record.CreatedAt,
            record.LastUpdatedAt,
            record.FailureDetails);
    }

    public async ValueTask<List<WorkflowHistoryEventRecord>> GetWorkflowHistoryAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        return await _historyStore.GetHistoryAsync(instanceId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TOutput> WaitForWorkflowCompletionAsync<TOutput>(
        WorkflowInstanceId instanceId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var maxWait = timeout ?? TimeSpan.FromMinutes(30);
        var startTime = _timeProvider.GetUtcNow();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = await _historyStore.GetStateAsync(instanceId, cancellationToken).ConfigureAwait(false);
            if (state is not null)
            {
                switch ((WorkflowStatus)state.Status)
                {
                    case WorkflowStatus.Completed:
                        return state.Output is { Length: > 0 }
                            ? _serializer.Deserialize<TOutput>(state.Output)!
                            : default!;

                    case WorkflowStatus.Failed:
                        throw new InvalidOperationException($"Workflow '{instanceId.Value}' failed: {state.FailureDetails}");

                    case WorkflowStatus.Terminated:
                        throw new InvalidOperationException($"Workflow '{instanceId.Value}' was terminated: {state.FailureDetails}");

                    case WorkflowStatus.Running:
                    case WorkflowStatus.Suspended:
                    default:
                        break;
                }
            }

            if (_timeProvider.GetUtcNow() - startTime > maxWait)
            {
                throw new TimeoutException($"Workflow '{instanceId.Value}' did not complete within {maxWait}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask RaiseEventAsync(
        WorkflowInstanceId instanceId,
        string eventName,
        object? eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var state = await _historyStore.GetStateAsync(instanceId, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            throw new KeyNotFoundException($"Workflow instance '{instanceId.Value}' was not found.");
        }

        var history = await _historyStore.GetHistoryAsync(instanceId, cancellationToken).ConfigureAwait(false);
        long nextId = (history.Count > 0 ? history[^1].EventId : 0) + 1;

        byte[]? data = null;
        if (eventData is not null)
        {
            data = eventData is byte[] b ? b : _serializer.Serialize(eventData);
        }

        var eventRecord = new WorkflowHistoryEventRecord
        {
            EventId = nextId,
            EventType = (int)WorkflowHistoryEventType.ExternalEventReceived,
            Name = eventName,
            Timestamp = _timeProvider.GetUtcNow(),
            Data = data
        };

        await _historyStore.AppendHistoryAsync(instanceId, [eventRecord], cancellationToken).ConfigureAwait(false);

        if (state.Status == (int)WorkflowStatus.Suspended &&
            string.Equals(state.WaitingEventName, eventName, StringComparison.OrdinalIgnoreCase))
        {
            state.WaitingEventName = null;
            state.Status = (int)WorkflowStatus.Running;
            state.LastUpdatedAt = _timeProvider.GetUtcNow();
            await _historyStore.SaveStateAsync(state, cancellationToken).ConfigureAwait(false);

            if (_registry.TryGetWorkflow(state.WorkflowName, out var def))
            {
                await _turnProcessor.ProcessTurnAsync(instanceId, def, state, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask FireTimerAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        _timerScheduler.CancelTimer(instanceId);
        if (_durableTimerStore is not null)
        {
            await _durableTimerStore.DeleteTimerAsync(instanceId, cancellationToken).ConfigureAwait(false);
        }

        var state = await _historyStore.GetStateAsync(instanceId, cancellationToken).ConfigureAwait(false);
        if (state is null) return;

        var history = await _historyStore.GetHistoryAsync(instanceId, cancellationToken).ConfigureAwait(false);
        long nextId = (history.Count > 0 ? history[^1].EventId : 0) + 1;

        var timerEvent = new WorkflowHistoryEventRecord
        {
            EventId = nextId,
            EventType = (int)WorkflowHistoryEventType.TimerFired,
            Name = "Timer",
            Timestamp = _timeProvider.GetUtcNow()
        };

        await _historyStore.AppendHistoryAsync(instanceId, [timerEvent], cancellationToken).ConfigureAwait(false);

        if (state.Status == (int)WorkflowStatus.Suspended)
        {
            state.TimerDueTime = null;
            state.Status = (int)WorkflowStatus.Running;
            state.LastUpdatedAt = _timeProvider.GetUtcNow();
            await _historyStore.SaveStateAsync(state, cancellationToken).ConfigureAwait(false);

            if (_registry.TryGetWorkflow(state.WorkflowName, out var def))
            {
                await _turnProcessor.ProcessTurnAsync(instanceId, def, state, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask TerminateWorkflowAsync(
        WorkflowInstanceId instanceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        _timerScheduler.CancelTimer(instanceId);
        if (_durableTimerStore is not null)
        {
            await _durableTimerStore.DeleteTimerAsync(instanceId, cancellationToken).ConfigureAwait(false);
        }

        var state = await _historyStore.GetStateAsync(instanceId, cancellationToken).ConfigureAwait(false);
        if (state is null) return;

        var history = await _historyStore.GetHistoryAsync(instanceId, cancellationToken).ConfigureAwait(false);
        long nextId = (history.Count > 0 ? history[^1].EventId : 0) + 1;

        var terminatedEvent = new WorkflowHistoryEventRecord
        {
            EventId = nextId,
            EventType = (int)WorkflowHistoryEventType.WorkflowTerminated,
            Name = state.WorkflowName,
            Timestamp = _timeProvider.GetUtcNow(),
            Details = reason
        };

        state.Status = (int)WorkflowStatus.Terminated;
        state.FailureDetails = reason;
        state.LastUpdatedAt = _timeProvider.GetUtcNow();

        await _historyStore.SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        await _historyStore.AppendHistoryAsync(instanceId, [terminatedEvent], cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PurgeWorkflowAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        _timerScheduler.CancelTimer(instanceId);
        if (_durableTimerStore is not null)
        {
            await _durableTimerStore.DeleteTimerAsync(instanceId, cancellationToken).ConfigureAwait(false);
        }
        await _historyStore.PurgeAsync(instanceId, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _timerScheduler.Dispose();
    }
}
