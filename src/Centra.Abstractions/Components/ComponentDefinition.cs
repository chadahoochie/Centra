namespace Centra.Components;

public sealed record ComponentDefinition
{
    public required string Name { get; init; }
    public required ComponentType Type { get; init; }
    public required string Provider { get; init; }
    public string Version { get; init; } = "v1";
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string>? SecretReferences { get; init; }
}
