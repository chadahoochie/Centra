namespace Centra.Actors;

/// <summary>
/// Manages durable reminders surviving actor deactivation, node restarts, and cluster failover.
/// </summary>
public interface IActorReminderManager
{
    ValueTask<ActorReminder> RegisterReminderAsync(string reminderName, ReadOnlyMemory<byte> state, TimeSpan dueTime, TimeSpan period, CancellationToken cancellationToken = default);
    ValueTask UnregisterReminderAsync(string reminderName, CancellationToken cancellationToken = default);
    ValueTask<ActorReminder?> GetReminderAsync(string reminderName, CancellationToken cancellationToken = default);
}
