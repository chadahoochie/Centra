namespace Centra.Bindings;

public sealed record BindingDefinition(
    string Name,
    string Type,
    BindingDirection Direction,
    IReadOnlyDictionary<string, string>? Metadata = null);
