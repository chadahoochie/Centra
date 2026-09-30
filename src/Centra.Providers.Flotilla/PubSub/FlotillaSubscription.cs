using Centra.PubSub;

namespace Centra.Providers.Flotilla.PubSub;

/// <summary>
/// Container for topic event handler registrations.
/// </summary>
public sealed class FlotillaSubscription
{
    public string Topic { get; }
    public Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> Handler { get; }
    public string? DeadLetterTopic { get; }
    public PubSubSubscribeOptions? Options { get; }

    public FlotillaSubscription(
        string topic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        string? deadLetterTopic = null,
        PubSubSubscribeOptions? options = null)
    {
        Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        DeadLetterTopic = deadLetterTopic;
        Options = options;
    }
}
