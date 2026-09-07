namespace Centra.Resilience;

/// <summary>
/// Defines an execution pipeline with fault tolerance and resilience strategies applied.
/// </summary>
public interface IResiliencePipeline
{
    /// <summary>
    /// Executes the specified asynchronous callback within the resilience pipeline.
    /// </summary>
    ValueTask ExecuteAsync(
        Func<CancellationToken, ValueTask> callback,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the specified asynchronous callback returning a result within the resilience pipeline.
    /// </summary>
    ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> callback,
        CancellationToken cancellationToken = default);
}
