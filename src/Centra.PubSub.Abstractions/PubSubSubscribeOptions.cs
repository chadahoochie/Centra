namespace Centra.PubSub;

/// <summary>
/// Fine-grained subscription options passed to <see cref="IPubSubSubscriber.SubscribeAsync"/>
/// to configure consumer concurrency, QoS buffering, and lifecycle policies.
/// </summary>
public sealed record PubSubSubscribeOptions
{
    /// <summary>
    /// Whether a subscription competes with other instances for messages or requires exactly one active consumer.
    /// </summary>
    public ConsumerMode ConsumerMode { get; init; } = ConsumerMode.CompetingConsumer;

    /// <summary>
    /// The number of unacknowledged messages the broker may deliver to this consumer before waiting for acknowledgments.
    /// When null, the provider's default prefetch setting is used (e.g. RabbitMQ defaults to 50).
    /// </summary>
    public int? PrefetchCount { get; init; }

    /// <summary>
    /// The maximum number of concurrent message handler invocations allowed on this consumer instance.
    /// Defaults to null, which falls back to the provider's default concurrency (usually 1 for sequential execution).
    /// </summary>
    public int? MaxConcurrentCalls { get; init; }

    /// <summary>
    /// Optional message time-to-live expiration before messages expire or route to dead-letter storage.
    /// </summary>
    public TimeSpan? MessageTimeToLive { get; init; }

    /// <summary>
    /// Whether the underlying queue or subscription should be automatically deleted when the last consumer disconnects.
    /// Useful for ephemeral consumers such as live telemetry or temporary cluster workers. Defaults to false.
    /// </summary>
    public bool AutoDelete { get; init; } = false;

    /// <summary>
    /// The number of times a handler returning <see cref="EventHandlingResult.Retry"/> may have the message
    /// redelivered before it is dead-lettered instead. The budget is enforced by the consumer, never by a
    /// broker delivery limit, because brokers do not count application-initiated requeues.
    /// When null, the provider's default budget is used (Centra providers default to 3).
    /// </summary>
    /// <remarks>
    /// Only the RabbitMQ driver enforces a redelivery budget today. Drivers that do not enforce it reject
    /// this option at subscribe time rather than ignoring it. Because a spent budget dead-letters, the RabbitMQ
    /// driver also refuses a subscription that has no dead-letter route to exhaust into.
    /// <para>
    /// Setting this to 0 disables the budget for the subscription and restores unbounded redelivery: a handler
    /// returning <see cref="EventHandlingResult.Retry"/> is redelivered forever and nothing is ever
    /// dead-lettered for exhaustion, so no dead-letter route is required. That is a deliberate decision to
    /// retry indefinitely, not a way to silence the missing-dead-letter-route error.
    /// </para>
    /// </remarks>
    public int? MaxRetryAttempts { get; init; }

    /// <summary>
    /// The delay applied before the first redelivery. Each subsequent retry doubles the previous delay,
    /// clamped to <see cref="RetryMaxBackoff"/>. When null, the provider's default initial backoff is used.
    /// </summary>
    public TimeSpan? RetryInitialBackoff { get; init; }

    /// <summary>
    /// The ceiling applied to the exponentially growing redelivery backoff.
    /// When null, the provider's default maximum backoff is used.
    /// </summary>
    /// <remarks>
    /// The backoff is awaited inside the consumer callback, and brokers dispatch callbacks sequentially per
    /// channel, so with <see cref="MaxConcurrentCalls"/> left at 1 a retrying message also delays the other
    /// deliveries on that subscription: this value directly bounds the worst-case head-of-line delay. Raise
    /// <see cref="MaxConcurrentCalls"/> above 1 to keep other messages flowing while one retries.
    /// </remarks>
    public TimeSpan? RetryMaxBackoff { get; init; }

    /// <summary>
    /// Optional provider-specific custom queue or subscription arguments (e.g., RabbitMQ x-arguments like quorum queues or max-length).
    /// </summary>
    public IReadOnlyDictionary<string, object?>? CustomArguments { get; init; }
}
