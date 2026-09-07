namespace Centra.Actors;

/// <summary>
/// Represents a durable reminder schedule for an actor.
/// </summary>
public readonly record struct ActorReminder(
    string Name,
    TimeSpan DueTime,
    TimeSpan Period,
    ReadOnlyMemory<byte> State = default);
