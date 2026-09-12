namespace Centra.PubSub.Outbox;

/// <summary>
/// Represents a message stored in the transactional outbox awaiting asynchronous publication.
/// </summary>
public sealed record OutboxMessage(
    string Id,
    string PubSubName,
    string Topic,
    ReadOnlyMemory<byte> Payload,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset CreatedAtUtc);
