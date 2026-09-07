using Centra.Actors;

namespace Centra.Core.Actors;

internal sealed class ActorReminderSchedule
{
    public ActorIdentity Identity { get; }
    public string Name { get; }
    public TimeSpan DueTime { get; }
    public TimeSpan Period { get; }
    public byte[]? State { get; }
    public DateTimeOffset NextDueUtc { get; set; }

    public ActorReminderSchedule(
        ActorIdentity identity,
        string name,
        TimeSpan dueTime,
        TimeSpan period,
        byte[]? state,
        DateTimeOffset nextDueUtc)
    {
        Identity = identity;
        Name = name;
        DueTime = dueTime;
        Period = period;
        State = state;
        NextDueUtc = nextDueUtc;
    }
}
