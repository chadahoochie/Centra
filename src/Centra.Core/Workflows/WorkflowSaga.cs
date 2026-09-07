using Centra.Workflows;
using Microsoft.Extensions.Logging;

namespace Centra.Core.Workflows;

/// <summary>
/// Manages registered compensation activities for distributed sagas in reverse order (LIFO).
/// </summary>
public sealed class WorkflowSaga : IWorkflowSaga
{
    private readonly WorkflowInstanceId _instanceId;
    private readonly IWorkflowActivityDispatcher _dispatcher;
    private readonly Action<WorkflowHistoryEventType, string, byte[]?, string?>? _onEvent;
    private readonly ILogger? _logger;
    private readonly List<SagaCompensationStep> _compensations = [];

    public IReadOnlyList<SagaCompensationStep> Compensations => _compensations;

    public WorkflowSaga(
        WorkflowInstanceId instanceId,
        IWorkflowActivityDispatcher dispatcher,
        Action<WorkflowHistoryEventType, string, byte[]?, string?>? onEvent = null,
        ILogger? logger = null)
    {
        _instanceId = instanceId;
        _dispatcher = dispatcher;
        _onEvent = onEvent;
        _logger = logger;
    }

    public void AddCompensation<TActivity, TInput>(TInput input)
        where TActivity : class, IWorkflowActivity<TInput, bool>
    {
        var name = typeof(TActivity).Name;
        AddCompensation(name, input);
    }

    public void AddCompensation(string activityName, object? input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);
        _compensations.Add(new SagaCompensationStep(activityName, input));
    }

    public async ValueTask CompensateAsync(CancellationToken cancellationToken = default)
    {
        if (_compensations.Count == 0) return;

        _logger?.LogInformation("Starting saga rollback for workflow instance '{InstanceId}' with {StepCount} compensation steps.",
            _instanceId.Value, _compensations.Count);

        _onEvent?.Invoke(WorkflowHistoryEventType.SagaCompensationStarted, "Saga", null, null);

        List<Exception>? exceptions = null;

        // Execute in reverse order (LIFO)
        for (int i = _compensations.Count - 1; i >= 0; i--)
        {
            var step = _compensations[i];
            _onEvent?.Invoke(WorkflowHistoryEventType.ActivityScheduled, step.ActivityName, null, null);
            try
            {
                _logger?.LogDebug("Executing compensation step '{ActivityName}' for instance '{InstanceId}'.",
                    step.ActivityName, _instanceId.Value);

                await _dispatcher.DispatchActivityAsync<object>(
                    _instanceId,
                    step.ActivityName,
                    step.Input,
                    options: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                _onEvent?.Invoke(WorkflowHistoryEventType.ActivityCompleted, step.ActivityName, null, null);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Compensation step '{ActivityName}' failed for instance '{InstanceId}'.",
                    step.ActivityName, _instanceId.Value);

                _onEvent?.Invoke(WorkflowHistoryEventType.ActivityFailed, step.ActivityName, null, ex.Message);
                exceptions ??= [];
                exceptions.Add(ex);
            }
        }

        _onEvent?.Invoke(WorkflowHistoryEventType.SagaCompensationCompleted, "Saga", null, null);

        if (exceptions is not null && exceptions.Count > 0)
        {
            throw new AggregateException("One or more saga compensations failed during rollback.", exceptions);
        }
    }
}
