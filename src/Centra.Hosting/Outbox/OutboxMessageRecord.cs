namespace Centra.Hosting.Outbox;

/// <summary>
/// Serializable DTO for persisting outbox messages in a state store.
/// </summary>
public sealed record OutboxMessageRecord(
    string Id,
    string PubSubName,
    string Topic,
    byte[] Payload,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset CreatedAtUtc);
