namespace Centra.Core.Actors;

internal sealed record ActorReminderRecord(
    string Name,
    TimeSpan DueTime,
    TimeSpan Period,
    byte[]? State);
