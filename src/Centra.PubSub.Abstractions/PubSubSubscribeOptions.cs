namespace Centra.PubSub;

/// <summary>
/// Whether a subscription competes with other instances for messages (default, matches every
/// driver's pre-existing behavior) or requires exactly one active consumer at a time.
/// </summary>
public enum ConsumerMode
{
    CompetingConsumer = 0,
    SingleActiveConsumer
}

public sealed record PubSubSubscribeOptions
{
    public ConsumerMode ConsumerMode { get; init; } = ConsumerMode.CompetingConsumer;
}
