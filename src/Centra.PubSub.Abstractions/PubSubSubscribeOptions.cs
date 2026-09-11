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
    /// Optional provider-specific custom queue or subscription arguments (e.g., RabbitMQ x-arguments like quorum queues or max-length).
    /// </summary>
    public IReadOnlyDictionary<string, object?>? CustomArguments { get; init; }
}
