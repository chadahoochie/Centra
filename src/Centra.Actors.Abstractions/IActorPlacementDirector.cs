namespace Centra.Actors;

/// <summary>
/// Resolves which cluster node hosts a given virtual actor identity.
/// </summary>
public interface IActorPlacementDirector
{
    string ResolveNodeId(ActorIdentity identity);
    bool IsLocal(ActorIdentity identity);
}
