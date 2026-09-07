using Centra.Actors;

namespace Centra.Core.Actors;

internal sealed class ActorActivation
{
    public ActorIdentity Identity { get; }
    public Actor Instance { get; }
    public ActorMailbox Mailbox { get; }
    public ActorStateManager StateManager { get; }
    public ActorTimerManager Timers { get; }
    public IActorReminderManager Reminders { get; }
    public DateTimeOffset LastAccessedUtc { get; set; }

    public ActorActivation(
        ActorIdentity identity,
        Actor instance,
        ActorMailbox mailbox,
        ActorStateManager stateManager,
        ActorTimerManager timers,
        IActorReminderManager reminders,
        DateTimeOffset lastAccessedUtc)
    {
        Identity = identity;
        Instance = instance;
        Mailbox = mailbox;
        StateManager = stateManager;
        Timers = timers;
        Reminders = reminders;
        LastAccessedUtc = lastAccessedUtc;
    }
}
