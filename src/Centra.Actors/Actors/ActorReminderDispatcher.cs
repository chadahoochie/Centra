using Centra.Actors;

namespace Centra.Core.Actors;

/// <summary>
/// Default implementation of <see cref="IActorReminderDispatcher"/> invoking the actor's <see cref="IRemindable"/> interface via <see cref="ActorManager"/>.
/// </summary>
internal sealed class ActorReminderDispatcher : IActorReminderDispatcher
{
    private readonly ActorManager _actorManager;

    public ActorReminderDispatcher(ActorManager actorManager)
    {
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
    }

    public async ValueTask DispatchReminderAsync(
        ActorReminderSchedule schedule,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        await _actorManager.DispatchAsync(
            schedule.Identity,
            async actor =>
            {
                if (actor is IRemindable remindable)
                {
                    await remindable.ReceiveReminderAsync(
                        schedule.Name,
                        schedule.State ?? Array.Empty<byte>(),
                        schedule.DueTime,
                        schedule.Period,
                        cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }
}
