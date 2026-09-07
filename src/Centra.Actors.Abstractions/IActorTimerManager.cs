namespace Centra.Actors;

/// <summary>
/// Manages ephemeral in-memory timers for active actor instances.
/// </summary>
public interface IActorTimerManager
{
    ActorTimer RegisterTimer(string timerName, Func<object?, ValueTask> callback, object? state, TimeSpan dueTime, TimeSpan period);
    ValueTask UnregisterTimerAsync(string timerName);
}
