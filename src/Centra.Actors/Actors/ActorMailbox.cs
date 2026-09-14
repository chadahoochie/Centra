using System.Threading.Channels;

namespace Centra.Core.Actors;

/// <summary>
/// Manages turn-based single-threaded execution for an active actor instance using a work-stealing
/// and cooperative turn-slice scheduling pattern to optimize thread pool utilization and throughput.
/// </summary>
public sealed class ActorMailbox : IAsyncDisposable
{
    public const int DefaultMaxTurnsPerSlice = 30;

    public const int StatusIdle = 0;
    public const int StatusRunning = 1;

    private readonly Channel<IActorTurn> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly int _maxTurnsPerSlice;
    private readonly object _gate = new();
    private Task _processingTask = Task.CompletedTask;
    private int _status = StatusIdle;
    private int _isDisposed;

    public ActorMailbox(int maxTurnsPerSlice = DefaultMaxTurnsPerSlice)
    {
        _maxTurnsPerSlice = maxTurnsPerSlice > 0 ? maxTurnsPerSlice : DefaultMaxTurnsPerSlice;
        _channel = Channel.CreateUnbounded<IActorTurn>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
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

        ScheduleIfNeeded();

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

        ScheduleIfNeeded();

        return new ValueTask<T>(turn.Task);
    }

    internal void ScheduleIfNeeded()
    {
        if (Interlocked.CompareExchange(ref _status, StatusRunning, StatusIdle) == StatusIdle)
        {
            lock (_gate)
            {
                _processingTask = Task.Run(ProcessTurnSliceAsync);
            }
        }
    }

    public async ValueTask<bool> DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _channel.Writer.TryComplete();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);

        try
        {
            Task taskToWait;
            lock (_gate)
            {
                if (_channel.Reader.TryPeek(out _) && Interlocked.CompareExchange(ref _status, StatusRunning, StatusIdle) == StatusIdle)
                {
                    _processingTask = Task.Run(ProcessTurnSliceAsync);
                }

                taskToWait = _processingTask;
            }

            await taskToWait.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal async Task ProcessTurnSliceAsync()
    {
        try
        {
            while (true)
            {
                int turnsProcessed = 0;
                while (turnsProcessed < _maxTurnsPerSlice && _channel.Reader.TryRead(out var turn))
                {
                    if (_cts.IsCancellationRequested)
                    {
                        turn.SetCanceled();
                        continue;
                    }

                    try
                    {
                        await turn.ExecuteAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceWarning("Actor turn execution encountered unhandled exception: {0}", ex.Message);
                    }

                    turnsProcessed++;
                }

                if (_cts.IsCancellationRequested)
                {
                    while (_channel.Reader.TryRead(out var remaining))
                    {
                        remaining.SetCanceled();
                    }
                    break;
                }

                // If slice limit was reached and more turns are pending, yield cooperatively to ThreadPool
                if (turnsProcessed >= _maxTurnsPerSlice && _channel.Reader.TryPeek(out _))
                {
                    await Task.Yield();
                    continue;
                }

                // Channel appears empty. Safely transition to idle under gate.
                lock (_gate)
                {
                    if (_channel.Reader.TryPeek(out _))
                    {
                        continue;
                    }

                    Interlocked.Exchange(ref _status, StatusIdle);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on mailbox shutdown
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _channel.Writer.TryComplete();
            await _cts.CancelAsync().ConfigureAwait(false);

            Task taskToWait;
            bool wasIdle;
            lock (_gate)
            {
                wasIdle = _status == StatusIdle;
                taskToWait = _processingTask;
            }

            if (wasIdle)
            {
                while (_channel.Reader.TryRead(out var remaining))
                {
                    remaining.SetCanceled();
                }
            }

            try
            {
                await taskToWait.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                System.Diagnostics.Trace.TraceWarning("Actor mailbox worker faulted during shutdown: {0}", ex.Message);
            }

            _cts.Dispose();
        }
    }
}
