using Centra.Actors;
using Centra.Locks;

namespace Centra.Core.Actors;

/// <summary>
/// Default implementation of <see cref="IActorReminderLockCoordinator"/> using <see cref="IDistributedLockProvider"/>.
/// </summary>
internal sealed class ActorReminderLockCoordinator : IActorReminderLockCoordinator
{
    private readonly IDistributedLockProvider? _lockProvider;
    private readonly ActorOptions _options;
    private readonly IActorReminderKeyFormatter _keyFormatter;

    public ActorReminderLockCoordinator(
        IDistributedLockProvider? lockProvider,
        ActorOptions options,
        IActorReminderKeyFormatter? keyFormatter = null)
    {
        _lockProvider = lockProvider;
        _options = options;
        _keyFormatter = keyFormatter ?? ActorReminderKeyFormatter.Instance;
    }

    public async ValueTask<(bool Acquired, IDistributedLock? Lock)> TryAcquireReminderLockAsync(
        ActorReminderSchedule schedule,
        CancellationToken cancellationToken)
    {
        if (_lockProvider is null)
        {
            return (true, null);
        }

        var lockKey = _keyFormatter.FormatLockKey(schedule.Identity, schedule.Name);
        try
        {
            var @lock = await _lockProvider.TryAcquireLockAsync(
                _options.DefaultLockStore,
                lockKey,
                expiryTime: TimeSpan.FromSeconds(30),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return (@lock is not null, @lock);
        }
        catch (InvalidOperationException)
        {
            // Lock store not configured; allow execution without distributed lock
            return (true, null);
        }
    }
}
