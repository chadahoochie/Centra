using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Centra.Actors;
using Centra.Diagnostics;
using Centra.Invocation;

namespace Centra.Core.Actors;

/// <summary>
/// Dynamic dispatch proxy intercepting interface calls and routing to local mailbox turns or remote RPC.
/// </summary>
public class ActorDispatchProxy : DispatchProxy, IActorProxy
{
    private static readonly ConcurrentDictionary<MethodInfo, ActorDispatchMethodMetadata> MetadataCache = new();

    private ActorIdentity _identity;
    private ActorManager _actorManager = null!;
    private IActorPlacementDirector _placementDirector = null!;
    private IServiceInvoker? _serviceInvoker;

    public ActorIdentity Identity => _identity;

    public void Initialize(
        ActorIdentity identity,
        ActorManager actorManager,
        IActorPlacementDirector placementDirector,
        IServiceInvoker? serviceInvoker = null)
    {
        _identity = identity;
        _actorManager = actorManager;
        _placementDirector = placementDirector;
        _serviceInvoker = serviceInvoker;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var metadata = MetadataCache.GetOrAdd(targetMethod, static m => ActorDispatchMethodMetadata.Create(m));

        using var activity = CentraDiagnostics.StartActorInvokeActivity(_identity.Type.Value, _identity.Id.Value, targetMethod.Name);

        var ct = metadata.CancellationTokenIndex >= 0 && args is not null && metadata.CancellationTokenIndex < args.Length
            ? (CancellationToken)args[metadata.CancellationTokenIndex]!
            : CancellationToken.None;

        var isLocal = _placementDirector.IsLocal(_identity);

        if (isLocal)
        {
            return metadata.LocalInvoker(_actorManager, _identity, targetMethod, args, ct);
        }

        if (_serviceInvoker == null)
        {
            throw new InvalidOperationException($"Cannot invoke remote actor '{_identity}' because no IServiceInvoker was configured.");
        }

        var targetNode = _placementDirector.ResolveNodeId(_identity);
        var path = $"centra/actors/{_identity.Type.Value}/{_identity.Id.Value}/method/{targetMethod.Name}";
        object? requestBody = args != null && args.Length == 1 ? args[0] : args;

        return metadata.RemoteInvoker(_serviceInvoker, targetNode, path, requestBody, ct);
    }
}
