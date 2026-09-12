using System.Collections.Concurrent;
using System.Reflection;
using Centra.Actors;
using Centra.Invocation;

namespace Centra.Core.Actors;

internal sealed class ActorDispatchMethodMetadata
{
    private static readonly ConcurrentDictionary<(Type ActorType, MethodInfo InterfaceMethod), MethodInfo> MethodResolutionCache = new();

    public required MethodInfo TargetMethod { get; init; }
    public required Type ReturnType { get; init; }
    public required int CancellationTokenIndex { get; init; }
    public required Func<ActorManager, ActorIdentity, MethodInfo, object?[]?, CancellationToken, object?> LocalInvoker { get; init; }
    public required Func<IServiceInvoker, string, string, object?, CancellationToken, object?> RemoteInvoker { get; init; }

    public static ActorDispatchMethodMetadata Create(MethodInfo targetMethod)
    {
        var parameters = targetMethod.GetParameters();
        var ctIndex = -1;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                ctIndex = i;
                break;
            }
        }

        var returnType = targetMethod.ReturnType;
        Func<ActorManager, ActorIdentity, MethodInfo, object?[]?, CancellationToken, object?> localInvoker;
        Func<IServiceInvoker, string, string, object?, CancellationToken, object?> remoteInvoker;

        if (returnType == typeof(Task))
        {
            localInvoker = InvokeLocalTaskVoid;
            remoteInvoker = InvokeRemoteTaskVoid;
        }
        else if (returnType == typeof(ValueTask))
        {
            localInvoker = InvokeLocalValueTaskVoid;
            remoteInvoker = InvokeRemoteValueTaskVoid;
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var localHelper = typeof(ActorDispatchMethodMetadata)
                .GetMethod(nameof(InvokeLocalTaskGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(resultType);
            localInvoker = (Func<ActorManager, ActorIdentity, MethodInfo, object?[]?, CancellationToken, object?>)
                Delegate.CreateDelegate(typeof(Func<ActorManager, ActorIdentity, MethodInfo, object?[]?, CancellationToken, object?>), localHelper);

            var remoteHelper = typeof(ActorDispatchMethodMetadata)
                .GetMethod(nameof(InvokeRemoteTaskGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(resultType);
            remoteInvoker = (Func<IServiceInvoker, string, string, object?, CancellationToken, object?>)
                Delegate.CreateDelegate(typeof(Func<IServiceInvoker, string, string, object?, CancellationToken, object?>), remoteHelper);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var localHelper = typeof(ActorDispatchMethodMetadata)
                .GetMethod(nameof(InvokeLocalValueTaskGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(resultType);
            localInvoker = (Func<ActorManager, ActorIdentity, MethodInfo, object?[]?, CancellationToken, object?>)
                Delegate.CreateDelegate(typeof(Func<ActorManager, ActorIdentity, MethodInfo, object?[]?, CancellationToken, object?>), localHelper);

            var remoteHelper = typeof(ActorDispatchMethodMetadata)
                .GetMethod(nameof(InvokeRemoteValueTaskGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(resultType);
            remoteInvoker = (Func<IServiceInvoker, string, string, object?, CancellationToken, object?>)
                Delegate.CreateDelegate(typeof(Func<IServiceInvoker, string, string, object?, CancellationToken, object?>), remoteHelper);
        }
        else
        {
            throw new NotSupportedException($"Actor method '{targetMethod.Name}' has unsupported return type '{returnType.Name}'.");
        }

        return new ActorDispatchMethodMetadata
        {
            TargetMethod = targetMethod,
            ReturnType = returnType,
            CancellationTokenIndex = ctIndex,
            LocalInvoker = localInvoker,
            RemoteInvoker = remoteInvoker
        };
    }

    internal static object? InvokeLocalTaskVoid(ActorManager manager, ActorIdentity identity, MethodInfo method, object?[]? args, CancellationToken ct)
    {
        return DispatchLocalVoidAsync(manager, identity, method, args, ct);
    }

    internal static object? InvokeLocalValueTaskVoid(ActorManager manager, ActorIdentity identity, MethodInfo method, object?[]? args, CancellationToken ct)
    {
        var task = DispatchLocalVoidAsync(manager, identity, method, args, ct);
        return new ValueTask(task);
    }

    internal static object? InvokeLocalTaskGeneric<T>(ActorManager manager, ActorIdentity identity, MethodInfo method, object?[]? args, CancellationToken ct)
    {
        return DispatchLocalGenericTaskAsync<T>(manager, identity, method, args, ct);
    }

    internal static object? InvokeLocalValueTaskGeneric<T>(ActorManager manager, ActorIdentity identity, MethodInfo method, object?[]? args, CancellationToken ct)
    {
        var task = DispatchLocalGenericTaskAsync<T>(manager, identity, method, args, ct);
        return new ValueTask<T>(task);
    }

    internal static async Task DispatchLocalVoidAsync(ActorManager manager, ActorIdentity identity, MethodInfo method, object?[]? args, CancellationToken ct)
    {
        await manager.DispatchAsync(
            identity,
            async actor =>
            {
                var actorMethod = ResolveActorMethod(actor.GetType(), method);
                try
                {
                    var raw = actorMethod.Invoke(actor, args);
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

    internal static async Task<T> DispatchLocalGenericTaskAsync<T>(ActorManager manager, ActorIdentity identity, MethodInfo method, object?[]? args, CancellationToken ct)
    {
        return await manager.DispatchAsync(
            identity,
            async actor =>
            {
                var actorMethod = ResolveActorMethod(actor.GetType(), method);
                try
                {
                    var raw = actorMethod.Invoke(actor, args);
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

    internal static async ValueTask<T> DispatchLocalGenericValueTaskAsync<T>(ActorManager manager, ActorIdentity identity, MethodInfo method, object?[]? args, CancellationToken ct)
    {
        return await DispatchLocalGenericTaskAsync<T>(manager, identity, method, args, ct).ConfigureAwait(false);
    }

    internal static object? InvokeRemoteTaskVoid(IServiceInvoker invoker, string targetNode, string path, object? body, CancellationToken ct)
    {
        var vt = invoker.InvokeMethodAsync<object, object?>(targetNode, path, body ?? new object(), "POST", null, ct);
        return vt.AsTask();
    }

    internal static object? InvokeRemoteValueTaskVoid(IServiceInvoker invoker, string targetNode, string path, object? body, CancellationToken ct)
    {
        var vt = invoker.InvokeMethodAsync<object, object?>(targetNode, path, body ?? new object(), "POST", null, ct);
        return new ValueTask(vt.AsTask());
    }

    internal static object? InvokeRemoteTaskGeneric<T>(IServiceInvoker invoker, string targetNode, string path, object? body, CancellationToken ct)
    {
        var vt = invoker.InvokeMethodAsync<object, T>(targetNode, path, body ?? new object(), "POST", null, ct);
        return vt.AsTask();
    }

    internal static object? InvokeRemoteValueTaskGeneric<T>(IServiceInvoker invoker, string targetNode, string path, object? body, CancellationToken ct)
    {
        return invoker.InvokeMethodAsync<object, T>(targetNode, path, body ?? new object(), "POST", null, ct);
    }

    internal static MethodInfo ResolveActorMethod(Type actorType, MethodInfo interfaceMethod)
    {
        return MethodResolutionCache.GetOrAdd((actorType, interfaceMethod), static key =>
        {
            var paramTypes = key.InterfaceMethod.GetParameters().Select(p => p.ParameterType).ToArray();
            return key.ActorType.GetMethod(key.InterfaceMethod.Name, paramTypes) ?? key.InterfaceMethod;
        });
    }
}
