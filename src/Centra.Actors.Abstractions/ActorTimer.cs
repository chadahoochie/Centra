namespace Centra.Actors;

/// <summary>
/// Handle to an active ephemeral in-memory actor timer.
/// </summary>
public sealed class ActorTimer : IAsyncDisposable, IDisposable
{
    private readonly Func<ValueTask> _disposeAsync;
    private int _disposed;

    public string Name { get; }
    public TimeSpan DueTime { get; }
    public TimeSpan Period { get; }

    public ActorTimer(string name, TimeSpan dueTime, TimeSpan period, Func<ValueTask> disposeAsync)
    {
        Name = name;
        DueTime = dueTime;
        Period = period;
        _disposeAsync = disposeAsync;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _disposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
