using System.Collections.Concurrent;
using Centra.PubSub;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// In-process <see cref="IRedeliveryBudget"/> keyed on the CloudEvents message id, bounded to a fixed number
/// of concurrently failing messages so a poison flood cannot grow the tracker without limit.
/// </summary>
/// <remarks>
/// Attempt state is deliberately process-local rather than carried on the message. Incrementing a CloudEvents
/// extension header would require republishing the event on every failure, which is a publish-path change, and
/// a republished copy loses its original delivery - and with it the queue's dead-letter routing and the
/// broker's own channel-failure backstop. The cost of keeping the count in process is that the budget resets
/// if the consumer restarts or the message is redelivered to a different replica; the loop is still bounded
/// per consumer, which is what the budget exists to guarantee.
/// </remarks>
public sealed class BoundedRedeliveryBudget : IRedeliveryBudget
{
    /// <summary>
    /// Default number of concurrently failing messages tracked before the oldest entries are evicted.
    /// </summary>
    public const int DefaultCapacity = 10_000;

    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly int _capacity;

    /// <summary>
    /// Creates a budget tracking at most <paramref name="capacity"/> concurrently failing messages.
    /// </summary>
    public BoundedRedeliveryBudget(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <inheritdoc />
    public RedeliveryDecision ChargeFailure(string messageId, in RedeliveryBudgetPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        if (policy.MaxRetryAttempts < 1)
        {
            _attempts.TryRemove(messageId, out _);
            return RedeliveryDecision.DeadLetterImmediately;
        }

        var retryNumber = _attempts.AddOrUpdate(messageId, 1, static (_, prior) => prior + 1);

        if (retryNumber == 1)
        {
            _insertionOrder.Enqueue(messageId);
            while (_insertionOrder.Count > _capacity && _insertionOrder.TryDequeue(out var evicted))
            {
                _attempts.TryRemove(evicted, out _);
            }
        }

        if (retryNumber > policy.MaxRetryAttempts)
        {
            _attempts.TryRemove(messageId, out _);
            return RedeliveryDecision.DeadLetterImmediately;
        }

        return new RedeliveryDecision(EventHandlingResult.Retry, policy.BackoffFor(retryNumber));
    }

    /// <inheritdoc />
    public void Forget(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        _attempts.TryRemove(messageId, out _);
    }
}
