namespace Centra.Actors;

/// <summary>
/// Marker interface for dynamic actor client proxies.
/// </summary>
public interface IActorProxy
{
    ActorIdentity Identity { get; }
}
