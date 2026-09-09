using Centra.Actors;

namespace Centra.Core.Actors;

/// <summary>
/// Default implementation of <see cref="IActorReminderKeyFormatter"/>.
/// </summary>
public sealed class ActorReminderKeyFormatter : IActorReminderKeyFormatter
{
    /// <summary>
    /// Singleton default instance of <see cref="ActorReminderKeyFormatter"/>.
    /// </summary>
    public static readonly ActorReminderKeyFormatter Instance = new();

    public string FormatStorageKey(ActorIdentity identity, string reminderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);
        return $"actor-reminders:{identity.Type.Value}:{identity.Id.Value}:{reminderName}";
    }

    public string FormatScheduleKey(ActorIdentity identity, string reminderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);
        return $"{identity.Type.Value}:{identity.Id.Value}:{reminderName}";
    }

    public string FormatLockKey(ActorIdentity identity, string reminderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);
        return $"actor-reminder:{identity}:{reminderName}:lock";
    }
}
