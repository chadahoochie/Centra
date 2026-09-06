namespace Centra.Events;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class EventContractAttribute(string type) : Attribute
{
    public string Type { get; } = type;
    public string? Version { get; init; }
    public string? Schema { get; init; }
}
