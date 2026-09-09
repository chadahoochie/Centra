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
    private readonly IActorReminderKeyFormatter _keyFormatter;
    private readonly IActorReminderScheduleCalculator _scheduleCalculator;
    private readonly IActorReminderLockCoordinator _lockCoordinator;
    private readonly IActorReminderDispatcher _dispatcher;
    private readonly ConcurrentDictionary<string, ActorReminderSchedule> _schedules = new(StringComparer.Ordinal);

    public ActorReminderCoordinator(
        ActorManager actorManager,
        IStateStore stateStore,
        ActorOptions options,
        IDistributedLockProvider? lockProvider = null,
        TimeProvider? timeProvider = null,
        IActorReminderKeyFormatter? keyFormatter = null)
        : this(
            actorManager,
            stateStore,
            options,
            lockProvider,
            timeProvider,
            keyFormatter,
            null,
            null,
            null)
    {
    }

    internal ActorReminderCoordinator(
        ActorManager actorManager,
        IStateStore stateStore,
        ActorOptions options,
        IDistributedLockProvider? lockProvider,
        TimeProvider? timeProvider,
        IActorReminderKeyFormatter? keyFormatter,
        IActorReminderScheduleCalculator? scheduleCalculator,
        IActorReminderLockCoordinator? lockCoordinator,
        IActorReminderDispatcher? dispatcher)
    {
        _actorManager = actorManager;
        _stateStore = stateStore;
        _options = options;
        _lockProvider = lockProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _keyFormatter = keyFormatter ?? ActorReminderKeyFormatter.Instance;
        _scheduleCalculator = scheduleCalculator ?? ActorReminderScheduleCalculator.Instance;
        _lockCoordinator = lockCoordinator ?? new ActorReminderLockCoordinator(lockProvider, options, _keyFormatter);
        _dispatcher = dispatcher ?? new ActorReminderDispatcher(actorManager);
    }

    public void RegisterReminder(
        ActorIdentity identity,
        string reminderName,
        TimeSpan dueTime,
        TimeSpan period,
        byte[]? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reminderName);

        var key = _keyFormatter.FormatScheduleKey(identity, reminderName);
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

        var key = _keyFormatter.FormatScheduleKey(identity, reminderName);
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

            if (await ProcessReminderTickAsync(key, schedule, cancellationToken).ConfigureAwait(false))
            {
                dueCount++;
            }
        }

        return dueCount;
    }

    /// <summary>
    /// Evaluates and executes a reminder tick for the given schedule, coordinating distributed lock acquisition and next period advancement.
    /// </summary>
    internal async ValueTask<bool> ProcessReminderTickAsync(
        string key,
        ActorReminderSchedule schedule,
        CancellationToken cancellationToken)
    {
        var (acquired, @lock) = await _lockCoordinator.TryAcquireReminderLockAsync(schedule, cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            // Another cluster replica is processing this tick
            AdvanceOrEvict(key, schedule);
            return false;
        }

        try
        {
            await _dispatcher.DispatchReminderAsync(schedule, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (@lock is not null)
            {
                await @lock.DisposeAsync().ConfigureAwait(false);
            }
        }

        AdvanceOrEvict(key, schedule);
        return true;
    }

    /// <summary>
    /// Advances the reminder to its next due time or evicts it from active schedules if it is non-periodic.
    /// </summary>
    internal void AdvanceOrEvict(string key, ActorReminderSchedule schedule)
    {
        if (!_scheduleCalculator.TryAdvanceSchedule(schedule, _timeProvider))
        {
            _schedules.TryRemove(key, out _);
        }
    }
}
