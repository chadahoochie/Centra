namespace Centra.Actors;

/// <summary>
/// Strongly typed virtual actor type name.
/// </summary>
public readonly record struct ActorType
{
    public string Value { get; }

    public ActorType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static implicit operator string(ActorType type) => type.Value;
    public static implicit operator ActorType(string value) => new(value);

    public static ActorType FromType<TActor>() => new(typeof(TActor).Name);
    public static ActorType FromType(Type type) => new(type.Name);

    public override string ToString() => Value ?? string.Empty;
}
