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
            if (now >= schedule.NextDueUtc)
            {
                if (_lockProvider != null)
                {
                    var lockKey = $"actor-reminder:{schedule.Identity}:{schedule.Name}:lock";
                    IDistributedLock? @lock = null;
                    var lockStoreConfigured = true;

                    try
                    {
                        @lock = await _lockProvider.TryAcquireLockAsync(
                            _options.DefaultLockStore,
                            lockKey,
                            expiryTime: TimeSpan.FromSeconds(30),
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                        lockStoreConfigured = false;
                    }

                    if (lockStoreConfigured && @lock == null)
                    {
                        // Another cluster replica is processing this tick
                        if (schedule.Period > TimeSpan.Zero)
                        {
                            schedule.NextDueUtc = _timeProvider.GetUtcNow() + schedule.Period;
                        }
                        continue;
                    }

                    if (@lock != null)
                    {
                        await using (@lock.ConfigureAwait(false))
                        {
                            await ExecuteReminderAsync(schedule, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await ExecuteReminderAsync(schedule, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await ExecuteReminderAsync(schedule, cancellationToken).ConfigureAwait(false);
                }

                dueCount++;

                if (schedule.Period > TimeSpan.Zero)
                {
                    schedule.NextDueUtc = _timeProvider.GetUtcNow() + schedule.Period;
                }
                else
                {
                    _schedules.TryRemove(key, out _);
                }
            }
        }

        return dueCount;
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
