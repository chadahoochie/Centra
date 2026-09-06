using Centra.PubSub;

namespace Centra.Providers.InMemory.PubSub;

internal readonly record struct InMemorySubscription(
    Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> Handler,
    string? DeadLetterTopic);
