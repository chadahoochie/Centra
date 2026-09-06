namespace Centra.Events;

public readonly record struct EventContext(
    string Id,
    string Topic,
    string PubSubName,
    string Source,
    string Type,
    DateTimeOffset Timestamp,
    string? CorrelationId,
    string? CausationId,
    string? TenantId,
    IReadOnlyDictionary<string, string> Headers);
