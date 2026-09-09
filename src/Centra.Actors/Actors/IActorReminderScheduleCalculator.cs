namespace Centra.Core.Actors;

/// <summary>
/// Defines a contract for advancing or evicting reminder schedules based on periodic intervals.
/// </summary>
internal interface IActorReminderScheduleCalculator
{
    /// <summary>
    /// Computes whether a reminder should continue and updates its <see cref="ActorReminderSchedule.NextDueUtc"/>.
    /// Returns <see langword="true"/> if the schedule was advanced for the next period, or <see langword="false"/> if it was a one-shot reminder that should be evicted.
    /// </summary>
    bool TryAdvanceSchedule(ActorReminderSchedule schedule, TimeProvider timeProvider);
}
