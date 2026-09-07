namespace Centra.Actors;

/// <summary>
/// Interface implemented by actors to receive callbacks when durable reminders fire.
/// </summary>
public interface IRemindable
{
    ValueTask ReceiveReminderAsync(string reminderName, ReadOnlyMemory<byte> state, TimeSpan dueTime, TimeSpan period, CancellationToken cancellationToken = default);
}
