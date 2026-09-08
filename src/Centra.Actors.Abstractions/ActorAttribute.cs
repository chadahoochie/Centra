namespace Centra.Actors;

/// <summary>
/// Marks a concrete <see cref="Actor"/> subclass for attribute-driven auto-registration.
/// Optional - actors remain registrable purely via <c>AddCentraActor&lt;TActor, TActorInterface&gt;</c>
/// when the domain interface is ambiguous or scanning is undesirable (e.g. AOT/trimming).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ActorAttribute : Attribute
{
    public string? Name { get; init; }
}
