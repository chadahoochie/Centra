namespace Centra.Actors;

/// <summary>
/// Factory for generating strongly typed dynamic actor client proxies.
/// </summary>
public interface IActorProxyFactory
{
    TActorInterface CreateActorProxy<TActorInterface>(ActorId actorId, ActorType? actorType = null)
        where TActorInterface : class, IActor;
}
