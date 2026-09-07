namespace Centra.Actors;

/// <summary>
/// Strongly typed virtual actor identifier.
/// </summary>
public readonly record struct ActorId
{
    public string Value { get; }

    public ActorId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static implicit operator string(ActorId id) => id.Value;
    public static implicit operator ActorId(string value) => new(value);

    public static ActorId Create() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value ?? string.Empty;
}
