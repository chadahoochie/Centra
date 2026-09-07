namespace Centra.Core.Actors;

internal sealed class ActorActionTurn : IActorTurn
{
    private readonly Func<ValueTask> _action;
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationToken CancellationToken { get; }
    public Task Task => _tcs.Task;

    public ActorActionTurn(Func<ValueTask> action, CancellationToken cancellationToken)
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
            await _action().ConfigureAwait(false);
            _tcs.TrySetResult();
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
