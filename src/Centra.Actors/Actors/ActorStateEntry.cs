namespace Centra.Core.Actors;

internal sealed class ActorStateEntry
{
    public object? Value { get; set; }
    public string? ETag { get; set; }
    public ActorStateStatus Status { get; set; }
    public Type ValueType { get; set; }

    public ActorStateEntry(object? value, string? eTag, ActorStateStatus status, Type valueType)
    {
        Value = value;
        ETag = eTag;
        Status = status;
        ValueType = valueType;
    }
}
