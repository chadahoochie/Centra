using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace Centra.Hosting.Routing;

internal sealed class ActorEndpointMethodInvoker
{
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, ActorEndpointMethodInvoker?>> Cache = new();

    private readonly Func<object, ReadOnlyMemory<byte>, CancellationToken, ValueTask<object?>> _invoker;

    public static ActorEndpointMethodInvoker? GetOrCreate(Type actorType, string methodName)
    {
        var innerCache = Cache.GetOrAdd(
            actorType,
            static _ => new ConcurrentDictionary<string, ActorEndpointMethodInvoker?>(StringComparer.OrdinalIgnoreCase));

        return innerCache.GetOrAdd(methodName, static (m, type) => Create(type, m), actorType);
    }

    internal ActorEndpointMethodInvoker(Func<object, ReadOnlyMemory<byte>, CancellationToken, ValueTask<object?>> invoker)
    {
        _invoker = invoker;
    }

    public ValueTask<object?> InvokeAsync(object actor, ReadOnlyMemory<byte> bodyBytes, CancellationToken cancellationToken)
    {
        return _invoker(actor, bodyBytes, cancellationToken);
    }

    internal static ActorEndpointMethodInvoker? Create(Type actorType, string methodName)
    {
        var method = actorType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));

        if (method is null)
        {
            return null;
        }

        var returnType = method.ReturnType;
        Func<object?, ValueTask<object?>> awaiter;

        if (returnType == typeof(Task))
        {
            awaiter = static async raw =>
            {
                if (raw is Task task)
                {
                    await task.ConfigureAwait(false);
                }
                return null;
            };
        }
        else if (returnType == typeof(ValueTask))
        {
            awaiter = static async raw =>
            {
                if (raw is ValueTask vt)
                {
                    await vt.ConfigureAwait(false);
                }
                return null;
            };
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var helper = typeof(ActorEndpointMethodInvoker)
                .GetMethod(nameof(AwaitTaskGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(resultType);
            awaiter = (Func<object?, ValueTask<object?>>)Delegate.CreateDelegate(typeof(Func<object?, ValueTask<object?>>), helper);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var helper = typeof(ActorEndpointMethodInvoker)
                .GetMethod(nameof(AwaitValueTaskGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(resultType);
            awaiter = (Func<object?, ValueTask<object?>>)Delegate.CreateDelegate(typeof(Func<object?, ValueTask<object?>>), helper);
        }
        else
        {
            awaiter = static raw => ValueTask.FromResult(raw);
        }

        var parameters = method.GetParameters();

        if (parameters.Length == 0)
        {
            return new ActorEndpointMethodInvoker(async (actor, _, _) =>
            {
                try
                {
                    var rawResult = method.Invoke(actor, null);
                    return await awaiter(rawResult).ConfigureAwait(false);
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    throw tie.InnerException;
                }
            });
        }

        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(CancellationToken))
        {
            return new ActorEndpointMethodInvoker(async (actor, _, ct) =>
            {
                try
                {
                    var rawResult = method.Invoke(actor, [ct]);
                    return await awaiter(rawResult).ConfigureAwait(false);
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    throw tie.InnerException;
                }
            });
        }

        if (parameters.Length == 1)
        {
            var paramType = parameters[0].ParameterType;
            return new ActorEndpointMethodInvoker(async (actor, body, _) =>
            {
                var paramValue = body.Length > 0
                    ? JsonSerializer.Deserialize(body.Span, paramType)
                    : (paramType.IsValueType && Nullable.GetUnderlyingType(paramType) == null ? Activator.CreateInstance(paramType) : null);

                try
                {
                    var rawResult = method.Invoke(actor, [paramValue]);
                    return await awaiter(rawResult).ConfigureAwait(false);
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    throw tie.InnerException;
                }
            });
        }

        if (parameters.Length == 2 && parameters[1].ParameterType == typeof(CancellationToken))
        {
            var paramType = parameters[0].ParameterType;
            return new ActorEndpointMethodInvoker(async (actor, body, ct) =>
            {
                var paramValue = body.Length > 0
                    ? JsonSerializer.Deserialize(body.Span, paramType)
                    : (paramType.IsValueType && Nullable.GetUnderlyingType(paramType) == null ? Activator.CreateInstance(paramType) : null);

                try
                {
                    var rawResult = method.Invoke(actor, [paramValue, ct]);
                    return await awaiter(rawResult).ConfigureAwait(false);
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    throw tie.InnerException;
                }
            });
        }

        // General fallback for multi-parameter methods
        return new ActorEndpointMethodInvoker(async (actor, body, ct) =>
        {
            var args = new object?[parameters.Length];
            var bodyConsumed = false;
            for (var i = 0; i < parameters.Length; i++)
            {
                var pType = parameters[i].ParameterType;
                if (pType == typeof(CancellationToken))
                {
                    args[i] = ct;
                }
                else if (!bodyConsumed)
                {
                    args[i] = body.Length > 0
                        ? JsonSerializer.Deserialize(body.Span, pType)
                        : (pType.IsValueType && Nullable.GetUnderlyingType(pType) == null ? Activator.CreateInstance(pType) : null);
                    bodyConsumed = true;
                }
            }

            try
            {
                var rawResult = method.Invoke(actor, args);
                return await awaiter(rawResult).ConfigureAwait(false);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                throw tie.InnerException;
            }
        });
    }

    internal static async ValueTask<object?> AwaitTaskGeneric<TResult>(object? raw)
    {
        if (raw is Task<TResult> task)
        {
            return await task.ConfigureAwait(false);
        }
        return null;
    }

    internal static async ValueTask<object?> AwaitValueTaskGeneric<TResult>(object? raw)
    {
        if (raw is ValueTask<TResult> vt)
        {
            return await vt.ConfigureAwait(false);
        }
        return null;
    }
}
