namespace Centra.Bindings;

public readonly record struct BindingData(
    ReadOnlyMemory<byte> Data,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? ContentType = null);
