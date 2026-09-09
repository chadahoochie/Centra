using Centra.Locks;

namespace Centra.Core.Actors;

/// <summary>
/// Defines a contract for coordinating distributed mutual exclusion for actor reminder execution across cluster nodes.
/// </summary>
internal interface IActorReminderLockCoordinator
{
    /// <summary>
    /// Attempts to acquire a distributed lock for the specified reminder schedule.
    /// </summary>
    ValueTask<(bool Acquired, IDistributedLock? Lock)> TryAcquireReminderLockAsync(
        ActorReminderSchedule schedule,
        CancellationToken cancellationToken);
}
