using Centra.Drivers;
using Centra.PubSub;

namespace Centra.Providers.InMemory.PubSub;

internal readonly record struct InMemorySubscription(
    Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> Handler,
    string? DeadLetterTopic,
    ConsumerMode ConsumerMode = ConsumerMode.CompetingConsumer,
    PubSubSubscribeOptions? Options = null)
{
    internal async ValueTask DispatchAsync(
        IPubSubDriver publisher,
        string pubSubName,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await Handler(payload, metadata, cancellationToken).ConfigureAwait(false);
            if (result == EventHandlingResult.DeadLetter && !string.IsNullOrWhiteSpace(DeadLetterTopic))
            {
                await publisher.PublishAsync(pubSubName, DeadLetterTopic, payload, metadata, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(DeadLetterTopic))
            {
                await publisher.PublishAsync(pubSubName, DeadLetterTopic, payload, metadata, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw;
            }
        }
    }
}
