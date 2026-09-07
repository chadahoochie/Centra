namespace Centra.Actors;

/// <summary>
/// Base class for virtual actor implementations.
/// </summary>
public abstract class Actor : IActor
{
    public ActorIdentity Identity { get; set; }
    public ActorId Id => Identity.Id;
    public ActorType Type => Identity.Type;

    public IActorStateManager StateManager { get; set; } = null!;
    public IActorTimerManager Timers { get; set; } = null!;
    public IActorReminderManager Reminders { get; set; } = null!;

    public virtual ValueTask OnActivateAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public virtual ValueTask OnDeactivateAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
