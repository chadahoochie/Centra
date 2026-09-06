namespace Centra.Bindings;

public readonly record struct BindingRequest(
    ReadOnlyMemory<byte> Data,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? Operation = null);
