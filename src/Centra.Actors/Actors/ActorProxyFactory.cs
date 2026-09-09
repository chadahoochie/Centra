using System.Reflection;
using Centra.Actors;
using Centra.Invocation;

namespace Centra.Core.Actors;

/// <summary>
/// Factory for creating strongly typed dynamic actor client proxies.
/// </summary>
public sealed class ActorProxyFactory : IActorProxyFactory
{
    private readonly ActorManager _actorManager;
    private readonly IActorPlacementDirector _placementDirector;
    private readonly IServiceInvoker? _serviceInvoker;

    public ActorProxyFactory(
        ActorManager actorManager,
        IActorPlacementDirector placementDirector,
        IServiceInvoker? serviceInvoker = null)
    {
        ArgumentNullException.ThrowIfNull(actorManager);
        ArgumentNullException.ThrowIfNull(placementDirector);

        _actorManager = actorManager;
        _placementDirector = placementDirector;
        _serviceInvoker = serviceInvoker;
    }

    public TActorInterface CreateActorProxy<TActorInterface>(ActorId actorId, ActorType? actorType = null)
        where TActorInterface : class, IActor
    {
        if (actorId.IsEmpty)
        {
            throw new ArgumentException("ActorId cannot be empty.", nameof(actorId));
        }

        var interfaceName = typeof(TActorInterface).Name;
        var defaultTypeName = interfaceName.StartsWith('I') && interfaceName.Length > 1
            ? interfaceName.Substring(1)
            : interfaceName;

        var resolvedType = actorType ?? new ActorType(defaultTypeName);
        var identity = new ActorIdentity(resolvedType, actorId);

        var proxy = DispatchProxy.Create<TActorInterface, ActorDispatchProxy>();
        ((ActorDispatchProxy)(object)proxy).Initialize(
            identity,
            _actorManager,
            _placementDirector,
            _serviceInvoker);

        return proxy;
    }
}
