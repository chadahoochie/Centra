using System.Collections.Concurrent;
using Centra.PubSub;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// In-process <see cref="IRedeliveryBudget"/> keyed on the subscription's queue plus the message id.
/// </summary>
/// <remarks>
/// <para>
/// Attempt state is deliberately process-local rather than carried on the message. Incrementing a CloudEvents
/// extension header would require republishing the event on every failure, which is a publish-path change, and
/// a republished copy loses its original delivery - and with it the queue's dead-letter routing and the
/// broker's own channel-failure backstop. The cost of keeping the count in process is that the budget resets
/// if the consumer restarts, and that a requeued message picked up by a different replica is counted
/// independently there, so a cluster of N replicas bounds a message at N budgets rather than one; the loop is
/// still bounded per consumer, which is what the budget exists to guarantee.
/// </para>
/// <para>
/// Most entries are removed the moment their delivery stops being retried - on success, on drop, on budget
/// exhaustion, on a drain that abandons the delivery, and wholesale for a queue whose subscription is torn
/// down. Some deliveries never come back at all though: a requeued message can be taken by another replica, or
/// expire under <c>x-message-ttl</c>, or be dead-lettered by the broker. Those entries are reclaimed by age
/// instead, once nothing could still legitimately charge them again (see
/// <see cref="RedeliveryBudgetPolicy.StaleAfter"/>). Age is the only reason an entry is ever forgotten: an
/// entry is never evicted to make room for a newer one, because insertion order says nothing about liveness
/// and rolling a live counter back to attempt one would hand a poison backlog the unbounded loop this type
/// exists to prevent. Under extreme cardinality the tracker instead stops admitting new messages and
/// dead-letters them, which is bounded, rather than losing count of the messages it is already bounding.
/// </para>
/// </remarks>
public sealed class BoundedRedeliveryBudget : IRedeliveryBudget
{
    /// <summary>
    /// Messages tracked concurrently before the tracker refuses to admit new ones and dead-letters them.
    /// </summary>
    public const int DefaultMaxTrackedMessages = 100_000;

    private readonly ConcurrentDictionary<RedeliveryBudgetKey, RedeliveryAttemptState> _attempts = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _maxTrackedMessages;
    private long _trackedCount;
    private long _lastSweepTicks;

    /// <summary>
    /// Creates a tracker reclaiming stale entries against <paramref name="timeProvider"/> and admitting at
    /// most <paramref name="maxTrackedMessages"/> concurrently retrying messages.
    /// </summary>
    public BoundedRedeliveryBudget(TimeProvider? timeProvider = null, int maxTrackedMessages = DefaultMaxTrackedMessages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTrackedMessages, 1);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxTrackedMessages = maxTrackedMessages;
        _lastSweepTicks = _timeProvider.GetUtcNow().UtcTicks;
    }

    /// <summary>
    /// Messages currently being counted. Exposed so the tracker's memory can be observed rather than assumed.
    /// </summary>
    public long TrackedMessageCount => Interlocked.Read(ref _trackedCount);

    /// <inheritdoc />
    public RedeliveryDecision ChargeFailure(in RedeliveryBudgetKey key, in RedeliveryBudgetPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key.QueueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.MessageId);

        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        var staleAfterTicks = policy.StaleAfter.Ticks;
        var lastSweepTicks = Interlocked.Read(ref _lastSweepTicks);

        if (nowTicks - lastSweepTicks > staleAfterTicks &&
            Interlocked.CompareExchange(ref _lastSweepTicks, nowTicks, lastSweepTicks) == lastSweepTicks)
        {
            foreach (var tracked in _attempts)
            {
                if (nowTicks - tracked.Value.LastChargedTicks > staleAfterTicks &&
                    _attempts.TryRemove(tracked))
                {
                    Interlocked.Decrement(ref _trackedCount);
                }
            }
        }

        while (true)
        {
            if (_attempts.TryGetValue(key, out var prior))
            {
                var retryNumber = nowTicks - prior.LastChargedTicks > staleAfterTicks ? 1 : prior.RetryNumber + 1;

                if (retryNumber > policy.MaxRetryAttempts)
                {
                    if (_attempts.TryRemove(key, out _))
                    {
                        Interlocked.Decrement(ref _trackedCount);
                    }

                    return RedeliveryDecision.DeadLetterImmediately;
                }

                if (_attempts.TryUpdate(key, new RedeliveryAttemptState(retryNumber, nowTicks), prior))
                {
                    return new RedeliveryDecision(EventHandlingResult.Retry, policy.BackoffFor(retryNumber));
                }

                continue;
            }

            if (policy.MaxRetryAttempts < 1 || Interlocked.Read(ref _trackedCount) >= _maxTrackedMessages)
            {
                return RedeliveryDecision.DeadLetterImmediately;
            }

            if (_attempts.TryAdd(key, new RedeliveryAttemptState(1, nowTicks)))
            {
                Interlocked.Increment(ref _trackedCount);
                return new RedeliveryDecision(EventHandlingResult.Retry, policy.BackoffFor(1));
            }
        }
    }

    /// <inheritdoc />
    public void Forget(in RedeliveryBudgetKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key.QueueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.MessageId);

        if (_attempts.TryRemove(key, out _))
        {
            Interlocked.Decrement(ref _trackedCount);
        }
    }

    /// <inheritdoc />
    public void ForgetQueue(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        foreach (var key in _attempts.Keys)
        {
            if (string.Equals(key.QueueName, queueName, StringComparison.Ordinal) &&
                _attempts.TryRemove(key, out _))
            {
                Interlocked.Decrement(ref _trackedCount);
            }
        }
    }
}
