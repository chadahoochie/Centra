using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Centra.Diagnostics;
using Centra.Serialization;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centra.Core.Workflows;

/// <summary>
/// Core execution engine coordinating workflow lifecycles, deterministic replay, state, and sagas.
/// </summary>
public sealed class WorkflowEngine : IWorkflowEngine, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowHistoryStore _historyStore;
    private readonly IWorkflowActivityDispatcher _dispatcher;
    private readonly ICentraSerializer _serializer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorkflowEngine>? _logger;
    private readonly ConcurrentDictionary<WorkflowInstanceId, ITimer> _activeTimers = new();

    public WorkflowEngine(
        IServiceProvider serviceProvider,
        IWorkflowRegistry registry,
        IWorkflowHistoryStore historyStore,
        IWorkflowActivityDispatcher dispatcher,
        ICentraSerializer serializer,
        TimeProvider? timeProvider = null,
        ILogger<WorkflowEngine>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _historyStore = historyStore;
        _dispatcher = dispatcher;
        _serializer = serializer;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
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

        await RunWorkflowTurnAsync(actualId, def, stateRecord, cancellationToken).ConfigureAwait(false);
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
                var status = (WorkflowStatus)state.Status;
                if (status == WorkflowStatus.Completed)
                {
                    if (state.Output is not null && state.Output.Length > 0)
                    {
                        return _serializer.Deserialize<TOutput>(state.Output)!;
                    }
                    return default!;
                }

                if (status == WorkflowStatus.Failed)
                {
                    throw new InvalidOperationException($"Workflow '{instanceId.Value}' failed: {state.FailureDetails}");
                }

                if (status == WorkflowStatus.Terminated)
                {
                    throw new InvalidOperationException($"Workflow '{instanceId.Value}' was terminated: {state.FailureDetails}");
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
                await RunWorkflowTurnAsync(instanceId, def, state, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask FireTimerAsync(
        WorkflowInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        if (_activeTimers.TryRemove(instanceId, out var timer))
        {
            timer.Dispose();
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
                await RunWorkflowTurnAsync(instanceId, def, state, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask TerminateWorkflowAsync(
        WorkflowInstanceId instanceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (_activeTimers.TryRemove(instanceId, out var timer))
        {
            timer.Dispose();
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
        if (_activeTimers.TryRemove(instanceId, out var timer))
        {
            timer.Dispose();
        }

        await _historyStore.PurgeAsync(instanceId, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RunWorkflowTurnAsync(
        WorkflowInstanceId instanceId,
        WorkflowDefinition def,
        WorkflowStateRecord stateRecord,
        CancellationToken cancellationToken)
    {
        object? workflowInstance;
        try
        {
            workflowInstance = _serviceProvider.GetService(def.WorkflowType)
                ?? ActivatorUtilities.CreateInstance(_serviceProvider, def.WorkflowType);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to instantiate workflow '{WorkflowName}'", def.Name);
            stateRecord.Status = (int)WorkflowStatus.Failed;
            stateRecord.FailureDetails = ex.Message;
            stateRecord.LastUpdatedAt = _timeProvider.GetUtcNow();
            await _historyStore.SaveStateAsync(stateRecord, cancellationToken).ConfigureAwait(false);
            return;
        }

        var pastHistory = await _historyStore.GetHistoryAsync(instanceId, cancellationToken).ConfigureAwait(false);
        var context = new DeterministicWorkflowContext(
            instanceId,
            def.Name,
            pastHistory,
            _dispatcher,
            _serializer,
            _timeProvider,
            cancellationToken);

        object? typedInput = null;
        if (stateRecord.Input is not null && stateRecord.Input.Length > 0 && def.InputType != typeof(void))
        {
            typedInput = _serializer.Deserialize(stateRecord.Input, def.InputType);
        }

        try
        {
            var output = await InvokeWorkflowRunAsync(workflowInstance, context, typedInput).ConfigureAwait(false);

            // Turn completed successfully
            stateRecord.Status = (int)WorkflowStatus.Completed;
            stateRecord.CustomStatus = context.CustomStatus;
            stateRecord.LastUpdatedAt = _timeProvider.GetUtcNow();

            byte[]? outputBytes = null;
            if (output is not null)
            {
                outputBytes = _serializer.Serialize(output);
                stateRecord.Output = outputBytes;
            }

            long nextId = (pastHistory.Count > 0 ? pastHistory[^1].EventId : 0) + context.NewEvents.Count + 1;
            var completedEvent = new WorkflowHistoryEventRecord
            {
                EventId = nextId,
                EventType = (int)WorkflowHistoryEventType.WorkflowCompleted,
                Name = def.Name,
                Timestamp = _timeProvider.GetUtcNow(),
                Data = outputBytes
            };
            context.NewEvents.Add(completedEvent);

            await _historyStore.AppendHistoryAsync(instanceId, context.NewEvents, cancellationToken).ConfigureAwait(false);
            await _historyStore.SaveStateAsync(stateRecord, cancellationToken).ConfigureAwait(false);
        }
        catch (WorkflowSuspendedException ex)
        {
            _logger?.LogDebug("Workflow '{InstanceId}' suspended: {Reason}", instanceId.Value, ex.Reason);

            stateRecord.Status = (int)WorkflowStatus.Suspended;
            stateRecord.CustomStatus = context.CustomStatus;
            stateRecord.WaitingEventName = context.WaitingEventName;
            stateRecord.TimerDueTime = context.TimerDueTime;
            stateRecord.LastUpdatedAt = _timeProvider.GetUtcNow();

            await _historyStore.AppendHistoryAsync(instanceId, context.NewEvents, cancellationToken).ConfigureAwait(false);
            await _historyStore.SaveStateAsync(stateRecord, cancellationToken).ConfigureAwait(false);

            if (context.TimerDueTime.HasValue)
            {
                var delay = context.TimerDueTime.Value - _timeProvider.GetUtcNow();
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                ScheduleTimer(instanceId, delay);
            }
        }
        catch (Exception ex)
        {
            var actualEx = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
            _logger?.LogError(actualEx, "Workflow '{InstanceId}' failed. Triggering saga compensations if registered.", instanceId.Value);

            // Trigger Saga compensations if registered
            if (context.Saga.Compensations.Count > 0)
            {
                long startId = (pastHistory.Count > 0 ? pastHistory[^1].EventId : 0) + context.NewEvents.Count + 1;
                context.NewEvents.Add(new WorkflowHistoryEventRecord
                {
                    EventId = startId,
                    EventType = (int)WorkflowHistoryEventType.SagaCompensationStarted,
                    Name = def.Name,
                    Timestamp = _timeProvider.GetUtcNow()
                });

                try
                {
                    await context.Saga.CompensateAsync(cancellationToken).ConfigureAwait(false);

                    long compId = (pastHistory.Count > 0 ? pastHistory[^1].EventId : 0) + context.NewEvents.Count + 1;
                    context.NewEvents.Add(new WorkflowHistoryEventRecord
                    {
                        EventId = compId,
                        EventType = (int)WorkflowHistoryEventType.SagaCompensationCompleted,
                        Name = def.Name,
                        Timestamp = _timeProvider.GetUtcNow()
                    });
                }
                catch (Exception compEx)
                {
                    _logger?.LogError(compEx, "Saga compensation failed for workflow '{InstanceId}'", instanceId.Value);
                }
            }

            stateRecord.Status = (int)WorkflowStatus.Failed;
            stateRecord.FailureDetails = actualEx.Message;
            stateRecord.LastUpdatedAt = _timeProvider.GetUtcNow();

            long failedId = (pastHistory.Count > 0 ? pastHistory[^1].EventId : 0) + context.NewEvents.Count + 1;
            context.NewEvents.Add(new WorkflowHistoryEventRecord
            {
                EventId = failedId,
                EventType = (int)WorkflowHistoryEventType.WorkflowFailed,
                Name = def.Name,
                Timestamp = _timeProvider.GetUtcNow(),
                Details = actualEx.Message
            });

            await _historyStore.AppendHistoryAsync(instanceId, context.NewEvents, cancellationToken).ConfigureAwait(false);
            await _historyStore.SaveStateAsync(stateRecord, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<object?> InvokeWorkflowRunAsync(
        object workflowInstance,
        IWorkflowContext context,
        object? typedInput)
    {
        var runMethod = workflowInstance.GetType().GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Instance);
        if (runMethod is null)
        {
            throw new InvalidOperationException($"Workflow '{workflowInstance.GetType().FullName}' does not have a public RunAsync method.");
        }

        object? taskObj;
        try
        {
            taskObj = runMethod.Invoke(workflowInstance, [context, typedInput]);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }
        if (taskObj is null) return null;

        var type = taskObj.GetType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var asTask = type.GetMethod("AsTask")?.Invoke(taskObj, null) as Task;
            if (asTask is not null)
            {
                await asTask.ConfigureAwait(false);
                return asTask.GetType().GetProperty("Result")?.GetValue(asTask);
            }
        }

        if (taskObj is ValueTask voidVt)
        {
            await voidVt.ConfigureAwait(false);
            return null;
        }

        if (taskObj is Task voidT)
        {
            await voidT.ConfigureAwait(false);
            var resultProp = taskObj.GetType().GetProperty("Result");
            return resultProp?.GetValue(taskObj);
        }

        return null;
    }

    private void ScheduleTimer(WorkflowInstanceId instanceId, TimeSpan delay)
    {
        if (_activeTimers.TryRemove(instanceId, out var existing))
        {
            existing.Dispose();
        }

        var timer = _timeProvider.CreateTimer(
            async state =>
            {
                var id = (WorkflowInstanceId)state!;
                try
                {
                    await FireTimerAsync(id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to fire timer for workflow '{InstanceId}'", id.Value);
                }
            },
            instanceId,
            delay,
            Timeout.InfiniteTimeSpan);

        _activeTimers[instanceId] = timer;
    }

    public void Dispose()
    {
        foreach (var timer in _activeTimers.Values)
        {
            timer.Dispose();
        }
        _activeTimers.Clear();
    }
}
