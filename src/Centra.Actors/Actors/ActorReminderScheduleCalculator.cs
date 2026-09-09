namespace Centra.Core.Actors;

/// <summary>
/// Default implementation of <see cref="IActorReminderScheduleCalculator"/>.
/// </summary>
internal sealed class ActorReminderScheduleCalculator : IActorReminderScheduleCalculator
{
    /// <summary>
    /// Singleton default instance of <see cref="ActorReminderScheduleCalculator"/>.
    /// </summary>
    public static readonly ActorReminderScheduleCalculator Instance = new();

    public bool TryAdvanceSchedule(ActorReminderSchedule schedule, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (schedule.Period > TimeSpan.Zero)
        {
            schedule.NextDueUtc = timeProvider.GetUtcNow() + schedule.Period;
            return true;
        }

        return false;
    }
}
