using System.Reflection;
using Centra.Serialization;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centra.Core.Workflows;

/// <summary>
/// Default implementation of <see cref="IWorkflowTurnProcessor"/> managing workflow orchestrator lifecycle turns and state transitions.
/// </summary>
public sealed class WorkflowTurnProcessor : IWorkflowTurnProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkflowHistoryStore _historyStore;
    private readonly IWorkflowActivityDispatcher _dispatcher;
    private readonly ICentraSerializer _serializer;
    private readonly IWorkflowRunnerInvoker _runnerInvoker;
    private readonly IWorkflowTimerScheduler _timerScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly Func<WorkflowInstanceId, ValueTask> _onTimerDue;
    private readonly IDurableWorkflowTimerStore? _durableTimerStore;
    private readonly ILogger? _logger;

    public WorkflowTurnProcessor(
        IServiceProvider serviceProvider,
        IWorkflowHistoryStore historyStore,
        IWorkflowActivityDispatcher dispatcher,
        ICentraSerializer serializer,
        IWorkflowRunnerInvoker runnerInvoker,
        IWorkflowTimerScheduler timerScheduler,
        TimeProvider timeProvider,
        Func<WorkflowInstanceId, ValueTask> onTimerDue,
        IDurableWorkflowTimerStore? durableTimerStore = null,
        ILogger? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _runnerInvoker = runnerInvoker ?? throw new ArgumentNullException(nameof(runnerInvoker));
        _timerScheduler = timerScheduler ?? throw new ArgumentNullException(nameof(timerScheduler));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _onTimerDue = onTimerDue ?? throw new ArgumentNullException(nameof(onTimerDue));
        _durableTimerStore = durableTimerStore;
        _logger = logger;
    }

    public async ValueTask ProcessTurnAsync(
        WorkflowInstanceId instanceId,
        WorkflowDefinition def,
        WorkflowStateRecord stateRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(stateRecord);

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
            var output = await _runnerInvoker.InvokeWorkflowRunAsync(workflowInstance, context, typedInput).ConfigureAwait(false);
            await HandleWorkflowCompletedAsync(instanceId, def, stateRecord, context, pastHistory, output, cancellationToken).ConfigureAwait(false);
        }
        catch (WorkflowSuspendedException ex)
        {
            await HandleWorkflowSuspendedAsync(instanceId, stateRecord, context, ex, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await HandleWorkflowFailedAsync(instanceId, def, stateRecord, context, pastHistory, ex, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask HandleWorkflowCompletedAsync(
        WorkflowInstanceId instanceId,
        WorkflowDefinition def,
        WorkflowStateRecord stateRecord,
        DeterministicWorkflowContext context,
        IReadOnlyList<WorkflowHistoryEventRecord> pastHistory,
        object? output,
        CancellationToken cancellationToken)
    {
        stateRecord.Status = (int)WorkflowStatus.Completed;
        stateRecord.CustomStatus = context.CustomStatus;
        stateRecord.LastUpdatedAt = _timeProvider.GetUtcNow();

        byte[]? outputBytes = output is not null ? _serializer.Serialize(output) : null;
        stateRecord.Output = outputBytes;

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

    public async ValueTask HandleWorkflowSuspendedAsync(
        WorkflowInstanceId instanceId,
        WorkflowStateRecord stateRecord,
        DeterministicWorkflowContext context,
        WorkflowSuspendedException ex,
        CancellationToken cancellationToken)
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
            if (_durableTimerStore is not null)
            {
                var timerRecord = new DurableWorkflowTimerRecord(
                    instanceId,
                    0,
                    context.TimerDueTime.Value,
                    _timeProvider.GetUtcNow());
                await _durableTimerStore.SaveTimerAsync(timerRecord, cancellationToken).ConfigureAwait(false);
            }

            var delay = context.TimerDueTime.Value - _timeProvider.GetUtcNow();
            _timerScheduler.ScheduleTimer(instanceId, delay < TimeSpan.Zero ? TimeSpan.Zero : delay, _onTimerDue);
        }
    }

    public async ValueTask HandleWorkflowFailedAsync(
        WorkflowInstanceId instanceId,
        WorkflowDefinition def,
        WorkflowStateRecord stateRecord,
        DeterministicWorkflowContext context,
        IReadOnlyList<WorkflowHistoryEventRecord> pastHistory,
        Exception ex,
        CancellationToken cancellationToken)
    {
        var actualEx = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
        _logger?.LogError(actualEx, "Workflow '{InstanceId}' failed. Triggering saga compensations if registered.", instanceId.Value);

        if (context.Saga.Compensations.Count > 0)
        {
            await ExecuteSagaCompensationsAsync(instanceId, def, context, pastHistory, cancellationToken).ConfigureAwait(false);
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

    public async ValueTask ExecuteSagaCompensationsAsync(
        WorkflowInstanceId instanceId,
        WorkflowDefinition def,
        DeterministicWorkflowContext context,
        IReadOnlyList<WorkflowHistoryEventRecord> pastHistory,
        CancellationToken cancellationToken)
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
}
