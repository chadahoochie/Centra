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
/// if the consumer restarts or the message is redelivered to a different replica; the loop is still bounded
/// per consumer, which is what the budget exists to guarantee.
/// </para>
/// <para>
/// The tracker holds no capacity cap: every entry is removed the moment its delivery stops being retried -
/// on success, on drop, on budget exhaustion, on a drain that abandons the delivery, and wholesale for a queue
/// whose subscription is torn down - so it only ever holds the messages currently mid-retry and drains as they
/// finish. A cap would have to evict those live entries, rolling their counters back to attempt one and handing
/// a large poison backlog exactly the unbounded loop the budget exists to prevent.
/// </para>
/// </remarks>
public sealed class BoundedRedeliveryBudget : IRedeliveryBudget
{
    private readonly ConcurrentDictionary<RedeliveryBudgetKey, int> _attempts = new();

    /// <inheritdoc />
    public RedeliveryDecision ChargeFailure(in RedeliveryBudgetKey key, in RedeliveryBudgetPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key.QueueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.MessageId);

        var retryNumber = _attempts.AddOrUpdate(key, 1, static (_, prior) => prior + 1);

        if (retryNumber > policy.MaxRetryAttempts)
        {
            _attempts.TryRemove(key, out _);
            return RedeliveryDecision.DeadLetterImmediately;
        }

        return new RedeliveryDecision(EventHandlingResult.Retry, policy.BackoffFor(retryNumber));
    }

    /// <inheritdoc />
    public void Forget(in RedeliveryBudgetKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key.QueueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.MessageId);
        _attempts.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public void ForgetQueue(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        foreach (var key in _attempts.Keys)
        {
            if (string.Equals(key.QueueName, queueName, StringComparison.Ordinal))
            {
                _attempts.TryRemove(key, out _);
            }
        }
    }
}
