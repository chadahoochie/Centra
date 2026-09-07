namespace Centra.Actors;

/// <summary>
/// Convenience extension methods for IActorReminderManager.
/// </summary>
public static class ActorReminderManagerExtensions
{
    public static ValueTask<ActorReminder> RegisterReminderAsync(
        this IActorReminderManager manager,
        string reminderName,
        TimeSpan dueTime,
        TimeSpan period,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.RegisterReminderAsync(reminderName, ReadOnlyMemory<byte>.Empty, dueTime, period, cancellationToken);
    }
}
