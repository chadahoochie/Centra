using System.Threading.Channels;

namespace Centra.Core.Actors;

/// <summary>
/// Manages turn-based single-threaded execution for an active actor instance.
/// </summary>
public sealed class ActorMailbox : IAsyncDisposable
{
    private readonly Channel<IActorTurn> _channel;
    private readonly Task _workerTask;
    private readonly CancellationTokenSource _cts = new();
    private int _isDisposed;

    public ActorMailbox()
    {
        _channel = Channel.CreateUnbounded<IActorTurn>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _workerTask = Task.Run(ProcessQueueAsync);
    }

    public ValueTask EnqueueTurnAsync(Func<ValueTask> action, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        var turn = new ActorActionTurn(action, cancellationToken);
        if (!_channel.Writer.TryWrite(turn))
        {
            return ValueTask.FromException(new InvalidOperationException("Mailbox channel is closed."));
        }

        return new ValueTask(turn.Task);
    }

    public ValueTask<T> EnqueueTurnAsync<T>(Func<ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<T>(cancellationToken);
        }

        var turn = new ActorFunctionTurn<T>(action, cancellationToken);
        if (!_channel.Writer.TryWrite(turn))
        {
            return ValueTask.FromException<T>(new InvalidOperationException("Mailbox channel is closed."));
        }

        return new ValueTask<T>(turn.Task);
    }

    public async ValueTask<bool> DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _channel.Writer.TryComplete();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);

        try
        {
            await _workerTask.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal async Task ProcessQueueAsync()
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var turn))
                {
                    await turn.ExecuteAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == _cts.Token)
        {
            // Normal termination: mailbox was signaled to shut down
            System.Diagnostics.Trace.TraceInformation("Actor mailbox loop terminated on shutdown.");
        }
        finally
        {
            // Drain remaining items as canceled if aborted
            while (_channel.Reader.TryRead(out var remaining))
            {
                remaining.SetCanceled();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _channel.Writer.TryComplete();
            await _cts.CancelAsync().ConfigureAwait(false);

            try
            {
                await _workerTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Worker shutdown cleanup: log worker fault during disposal
                System.Diagnostics.Trace.TraceWarning("Actor mailbox worker faulted during shutdown: {0}", ex.Message);
            }

            _cts.Dispose();
        }
    }
}
