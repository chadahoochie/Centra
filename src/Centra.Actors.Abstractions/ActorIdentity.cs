namespace Centra.Actors;

/// <summary>
/// Composite identity uniquely identifying a virtual actor instance across the cluster.
/// </summary>
public readonly record struct ActorIdentity
{
    public ActorType Type { get; }
    public ActorId Id { get; }

    public ActorIdentity(ActorType type, ActorId id)
    {
        if (type.IsEmpty)
        {
            throw new ArgumentException("ActorType cannot be empty.", nameof(type));
        }

        if (id.IsEmpty)
        {
            throw new ArgumentException("ActorId cannot be empty.", nameof(id));
        }

        Type = type;
        Id = id;
    }

    public override string ToString() => $"{Type.Value}/{Id.Value}";
}
