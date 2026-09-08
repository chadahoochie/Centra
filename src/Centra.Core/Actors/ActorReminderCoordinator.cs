using System.Collections.Concurrent;
using Centra.Actors;
using Centra.Locks;
using Centra.State;

namespace Centra.Core.Actors;

/// <summary>
/// Coordinates durable reminder evaluation and dispatches callbacks to target virtual actors.
/// </summary>
public sealed class ActorReminderCoordinator
{
    private readonly ActorManager _actorManager;
    private readonly IStateStore _stateStore;
    private readonly ActorOptions _options;
    private readonly IDistributedLockProvider? _lockProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, ActorReminderSchedule> _schedules = new(StringComparer.Ordinal);

    public ActorReminderCoordinator(
        ActorManager actorManager,
        IStateStore stateStore,
        ActorOptions options,
        IDistributedLockProvider? lockProvider = null,
        TimeProvider? timeProvider = null)
    {
        _actorManager = actorManager;
        _stateStore = stateStore;
        _options = options;
        _lockProvider = lockProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void RegisterReminder(
        ActorIdentity identity,
        string reminderName,
        TimeSpan dueTime,
        TimeSpan period,
        byte[]? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);

        var key = FormatKey(identity, reminderName);
        var nextDueUtc = _timeProvider.GetUtcNow() + dueTime;

        _schedules[key] = new ActorReminderSchedule(
            identity,
            reminderName,
            dueTime,
            period,
            state,
            nextDueUtc);
    }

    public bool UnregisterReminder(ActorIdentity identity, string reminderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);

        var key = FormatKey(identity, reminderName);
        return _schedules.TryRemove(key, out _);
    }

    public async ValueTask<int> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var dueCount = 0;

        foreach (var (key, schedule) in _schedules)
        {
            if (now < schedule.NextDueUtc)
            {
                continue;
            }

            if (await TryProcessReminderAsync(key, schedule, cancellationToken).ConfigureAwait(false))
            {
                dueCount++;
            }
        }

        return dueCount;
    }

    private async ValueTask<bool> TryProcessReminderAsync(
        string key,
        ActorReminderSchedule schedule,
        CancellationToken cancellationToken)
    {
        if (_lockProvider is null)
        {
            await ExecuteReminderAsync(schedule, cancellationToken).ConfigureAwait(false);
            AdvanceOrEvictReminder(key, schedule);
            return true;
        }

        var (acquired, @lock) = await TryAcquireReminderLockAsync(schedule, cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            // Another cluster replica is processing this tick
            AdvanceOrEvictReminder(key, schedule);
            return false;
        }

        try
        {
            await ExecuteReminderAsync(schedule, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (@lock is not null)
            {
                await @lock.DisposeAsync().ConfigureAwait(false);
            }
        }

        AdvanceOrEvictReminder(key, schedule);
        return true;
    }

    private async ValueTask<(bool Acquired, IDistributedLock? Lock)> TryAcquireReminderLockAsync(
        ActorReminderSchedule schedule,
        CancellationToken cancellationToken)
    {
        var lockKey = $"actor-reminder:{schedule.Identity}:{schedule.Name}:lock";
        try
        {
            var @lock = await _lockProvider!.TryAcquireLockAsync(
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

    private void AdvanceOrEvictReminder(string key, ActorReminderSchedule schedule)
    {
        if (schedule.Period > TimeSpan.Zero)
        {
            schedule.NextDueUtc = _timeProvider.GetUtcNow() + schedule.Period;
        }
        else
        {
            _schedules.TryRemove(key, out _);
        }
    }

    private async ValueTask ExecuteReminderAsync(ActorReminderSchedule schedule, CancellationToken cancellationToken)
    {
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

    private static string FormatKey(ActorIdentity identity, string reminderName) =>
        $"{identity.Type.Value}:{identity.Id.Value}:{reminderName}";
}
