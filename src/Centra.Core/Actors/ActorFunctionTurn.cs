namespace Centra.Core.Actors;

internal sealed class ActorFunctionTurn<T> : IActorTurn
{
    private readonly Func<ValueTask<T>> _action;
    private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationToken CancellationToken { get; }
    public Task<T> Task => _tcs.Task;

    public ActorFunctionTurn(Func<ValueTask<T>> action, CancellationToken cancellationToken)
    {
        _action = action;
        CancellationToken = cancellationToken;
    }

    public async ValueTask ExecuteAsync()
    {
        if (CancellationToken.IsCancellationRequested)
        {
            _tcs.TrySetCanceled(CancellationToken);
            return;
        }

        try
        {
            var result = await _action().ConfigureAwait(false);
            _tcs.TrySetResult(result);
        }
        catch (OperationCanceledException oce) when (oce.CancellationToken == CancellationToken)
        {
            _tcs.TrySetCanceled(CancellationToken);
        }
        catch (Exception ex)
        {
            _tcs.TrySetException(ex);
        }
    }

    public void SetCanceled()
    {
        _tcs.TrySetCanceled(CancellationToken);
    }
}
