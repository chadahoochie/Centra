using System.Collections.Concurrent;
using Centra.Actors;

namespace Centra.Core.Actors;

/// <summary>
/// Manages ephemeral in-memory timers for active actor instances.
/// </summary>
public sealed class ActorTimerManager : IActorTimerManager, IAsyncDisposable
{
    private readonly ActorIdentity _identity;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, (ITimer Timer, ActorTimer ActorTimer)> _timers = new(StringComparer.Ordinal);
    private int _isDisposed;

    public ActorTimerManager(ActorIdentity identity, TimeProvider timeProvider)
    {
        _identity = identity;
        _timeProvider = timeProvider;
    }

    public ActorTimer RegisterTimer(
        string timerName,
        Func<object?, ValueTask> callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timerName);
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

        UnregisterTimerAsync(timerName).AsTask().GetAwaiter().GetResult();

        ITimer? underlyingTimer = null;
        var actorTimer = new ActorTimer(timerName, dueTime, period, () => UnregisterTimerAsync(timerName));

        underlyingTimer = _timeProvider.CreateTimer(
            async s =>
            {
                try
                {
                    await callback(s).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Timer callbacks should not bring down the actor host process
                    System.Diagnostics.Trace.TraceWarning("Actor timer '{0}' callback failed: {1}", timerName, ex.Message);
                }
            },
            state,
            dueTime,
            period);

        _timers[timerName] = (underlyingTimer, actorTimer);
        return actorTimer;
    }

    public ValueTask UnregisterTimerAsync(string timerName)
    {
        if (_timers.TryRemove(timerName, out var entry))
        {
            entry.Timer.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            foreach (var key in _timers.Keys)
            {
                await UnregisterTimerAsync(key).ConfigureAwait(false);
            }
        }
    }
}
