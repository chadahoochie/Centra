namespace Centra.Providers.InMemory.State;

internal readonly record struct InMemoryStateRecord(
    byte[] Value,
    string ETag,
    DateTimeOffset? ExpiresAt,
    IReadOnlyDictionary<string, string>? Metadata);
