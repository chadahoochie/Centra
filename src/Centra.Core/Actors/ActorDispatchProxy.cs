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

        using var activity = CentraDiagnostics.StartActorInvokeActivity(_identity.Type.Value, _identity.Id.Value, targetMethod.Name);

        var ct = ExtractCancellationToken(args);
        var returnType = targetMethod.ReturnType;
        var isLocal = _placementDirector.IsLocal(_identity);

        if (isLocal)
        {
            return InvokeLocal(targetMethod, args, returnType, ct);
        }

        return InvokeRemote(targetMethod, args, returnType, ct);
    }

    private object? InvokeLocal(MethodInfo targetMethod, object?[]? args, Type returnType, CancellationToken ct)
    {
        if (returnType == typeof(Task))
        {
            return DispatchLocalVoidAsync(targetMethod, args, ct);
        }

        if (returnType == typeof(ValueTask))
        {
            var task = DispatchLocalVoidAsync(targetMethod, args, ct);
            return new ValueTask(task);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var helperMethod = typeof(ActorDispatchProxy)
                .GetMethod(nameof(DispatchLocalGenericTaskAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(resultType);

            return helperMethod.Invoke(this, [targetMethod, args, ct]);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var helperMethod = typeof(ActorDispatchProxy)
                .GetMethod(nameof(DispatchLocalGenericValueTaskAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(resultType);

            return helperMethod.Invoke(this, [targetMethod, args, ct]);
        }

        throw new NotSupportedException($"Actor method '{targetMethod.Name}' has unsupported return type '{returnType.Name}'.");
    }

    private async Task DispatchLocalVoidAsync(MethodInfo targetMethod, object?[]? args, CancellationToken ct)
    {
        await _actorManager.DispatchAsync(
            _identity,
            async actor =>
            {
                var method = ResolveActorMethod(actor.GetType(), targetMethod);
                try
                {
                    var raw = method.Invoke(actor, args);
                    if (raw is Task t)
                    {
                        await t.ConfigureAwait(false);
                    }
                    else if (raw is ValueTask vt)
                    {
                        await vt.ConfigureAwait(false);
                    }
                }
                catch (TargetInvocationException tie)
                {
                    throw tie.InnerException ?? tie;
                }
            },
            ct).ConfigureAwait(false);
    }

    private async Task<T> DispatchLocalGenericTaskAsync<T>(MethodInfo targetMethod, object?[]? args, CancellationToken ct)
    {
        return await _actorManager.DispatchAsync(
            _identity,
            async actor =>
            {
                var method = ResolveActorMethod(actor.GetType(), targetMethod);
                try
                {
                    var raw = method.Invoke(actor, args);
                    if (raw is Task<T> taskT)
                    {
                        return await taskT.ConfigureAwait(false);
                    }

                    if (raw is ValueTask<T> vtT)
                    {
                        return await vtT.ConfigureAwait(false);
                    }

                    return (T)raw!;
                }
                catch (TargetInvocationException tie)
                {
                    throw tie.InnerException ?? tie;
                }
            },
            ct).ConfigureAwait(false);
    }

    private async ValueTask<T> DispatchLocalGenericValueTaskAsync<T>(MethodInfo targetMethod, object?[]? args, CancellationToken ct)
    {
        return await DispatchLocalGenericTaskAsync<T>(targetMethod, args, ct).ConfigureAwait(false);
    }

    private object? InvokeRemote(MethodInfo targetMethod, object?[]? args, Type returnType, CancellationToken ct)
    {
        if (_serviceInvoker == null)
        {
            throw new InvalidOperationException($"Cannot invoke remote actor '{_identity}' because no IServiceInvoker was configured.");
        }

        var targetNode = _placementDirector.ResolveNodeId(_identity);
        var path = $"centra/actors/{_identity.Type.Value}/{_identity.Id.Value}/method/{targetMethod.Name}";
        object? requestBody = args != null && args.Length == 1 ? args[0] : args;

        if (returnType == typeof(Task))
        {
            var vt = _serviceInvoker.InvokeMethodAsync<object, object?>(
                targetNode, path, requestBody ?? new object(), "POST", null, ct);
            return vt.AsTask();
        }

        if (returnType == typeof(ValueTask))
        {
            var vt = _serviceInvoker.InvokeMethodAsync<object, object?>(
                targetNode, path, requestBody ?? new object(), "POST", null, ct);
            return new ValueTask(vt.AsTask());
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var responseType = returnType.GetGenericArguments()[0];
            var invokeMethod = typeof(IServiceInvoker).GetMethod(nameof(IServiceInvoker.InvokeMethodAsync))!
                .MakeGenericMethod(typeof(object), responseType);

            var vt = invokeMethod.Invoke(_serviceInvoker, [targetNode, path, requestBody ?? new object(), "POST", null, ct])!;
            var asTaskMethod = vt.GetType().GetMethod(nameof(ValueTask<object>.AsTask))!;
            return asTaskMethod.Invoke(vt, null);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var responseType = returnType.GetGenericArguments()[0];
            var invokeMethod = typeof(IServiceInvoker).GetMethod(nameof(IServiceInvoker.InvokeMethodAsync))!
                .MakeGenericMethod(typeof(object), responseType);

            return invokeMethod.Invoke(_serviceInvoker, [targetNode, path, requestBody ?? new object(), "POST", null, ct]);
        }

        throw new NotSupportedException($"Actor method '{targetMethod.Name}' has unsupported return type '{returnType.Name}'.");
    }

    private static MethodInfo ResolveActorMethod(Type actorType, MethodInfo interfaceMethod)
    {
        var paramTypes = interfaceMethod.GetParameters().Select(p => p.ParameterType).ToArray();
        return actorType.GetMethod(interfaceMethod.Name, paramTypes) ?? interfaceMethod;
    }

    private static CancellationToken ExtractCancellationToken(object?[]? args)
    {
        if (args is null) return CancellationToken.None;
        foreach (var arg in args)
        {
            if (arg is CancellationToken ct) return ct;
        }
        return CancellationToken.None;
    }
}
