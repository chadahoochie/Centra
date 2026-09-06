namespace Centra.State;

public readonly record struct StateEntry<T>(
    string Key,
    T Value,
    string ETag,
    IReadOnlyDictionary<string, string>? Metadata = null);
