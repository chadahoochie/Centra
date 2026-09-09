namespace Centra.Core.Actors;

/// <summary>
/// Defines a contract for dispatching reminder callbacks to target virtual actor instances.
/// </summary>
internal interface IActorReminderDispatcher
{
    /// <summary>
    /// Dispatches a reminder callback to the target virtual actor.
    /// </summary>
    ValueTask DispatchReminderAsync(
        ActorReminderSchedule schedule,
        CancellationToken cancellationToken);
}
