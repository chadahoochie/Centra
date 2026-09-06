namespace Centra.Bindings;

public readonly record struct BindingResponse(
    ReadOnlyMemory<byte> Data,
    IReadOnlyDictionary<string, string>? Metadata = null);
