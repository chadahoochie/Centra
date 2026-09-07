namespace Centra.Workflows;

/// <summary>
/// Manages compensating actions for distributed sagas in reverse execution order (LIFO).
/// </summary>
public interface IWorkflowSaga
{
    IReadOnlyList<SagaCompensationStep> Compensations { get; }

    void AddCompensation<TActivity, TInput>(TInput input)
        where TActivity : class, IWorkflowActivity<TInput, bool>;

    void AddCompensation(string activityName, object? input);

    ValueTask CompensateAsync(CancellationToken cancellationToken = default);
}
