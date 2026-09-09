using Centra.Actors;

namespace Centra.Core.Actors;

/// <summary>
/// Defines a contract for formatting keys associated with actor reminders.
/// </summary>
public interface IActorReminderKeyFormatter
{
    /// <summary>
    /// Formats the state persistence storage key for an actor reminder.
    /// </summary>
    string FormatStorageKey(ActorIdentity identity, string reminderName);

    /// <summary>
    /// Formats the memory schedule dictionary key for an actor reminder.
    /// </summary>
    string FormatScheduleKey(ActorIdentity identity, string reminderName);

    /// <summary>
    /// Formats the distributed lock resource key for coordinating reminder execution across nodes.
    /// </summary>
    string FormatLockKey(ActorIdentity identity, string reminderName);
}
