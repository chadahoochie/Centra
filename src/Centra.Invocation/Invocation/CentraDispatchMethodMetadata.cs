using System.Reflection;

namespace Centra.Invocation;

internal sealed class CentraDispatchMethodMetadata
{
    public required string MethodName { get; init; }
    public required string HttpVerb { get; init; }
    public required int CancellationTokenIndex { get; init; }
    public required int BodyParameterIndex { get; init; }
    public required Func<IServiceInvoker, string, string, string, object?, CancellationToken, object?> Invoker { get; init; }

    public static CentraDispatchMethodMetadata Create(MethodInfo targetMethod)
    {
        var methodAttr = targetMethod.GetCustomAttribute<ServiceMethodAttribute>();
        var methodName = methodAttr?.Method ?? targetMethod.Name;
        var verb = methodAttr?.HttpVerb ?? "POST";

        var parameters = targetMethod.GetParameters();
        var ctIndex = -1;
        var bodyIndex = -1;

        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                ctIndex = i;
            }
            else if (bodyIndex < 0)
            {
                bodyIndex = i;
            }
        }

        var reqType = bodyIndex >= 0 ? parameters[bodyIndex].ParameterType : typeof(object);
        var returnType = targetMethod.ReturnType;

        Func<IServiceInvoker, string, string, string, object?, CancellationToken, object?> invoker;

        if (returnType.IsGenericType && (returnType.GetGenericTypeDefinition() == typeof(Task<>) || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            var respType = returnType.GetGenericArguments()[0];
            var helperName = returnType.GetGenericTypeDefinition() == typeof(Task<>)
                ? nameof(InvokeTaskGeneric)
                : nameof(InvokeValueTaskGeneric);

            var helperMethod = typeof(CentraDispatchMethodMetadata).GetMethod(helperName, BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(reqType, respType);

            invoker = (Func<IServiceInvoker, string, string, string, object?, CancellationToken, object?>)
                Delegate.CreateDelegate(typeof(Func<IServiceInvoker, string, string, string, object?, CancellationToken, object?>), helperMethod);
        }
        else if (returnType == typeof(Task))
        {
            var helperMethod = typeof(CentraDispatchMethodMetadata).GetMethod(nameof(InvokeTaskVoid), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(reqType);

            invoker = (Func<IServiceInvoker, string, string, string, object?, CancellationToken, object?>)
                Delegate.CreateDelegate(typeof(Func<IServiceInvoker, string, string, string, object?, CancellationToken, object?>), helperMethod);
        }
        else if (returnType == typeof(ValueTask))
        {
            var helperMethod = typeof(CentraDispatchMethodMetadata).GetMethod(nameof(InvokeValueTaskVoid), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(reqType);

            invoker = (Func<IServiceInvoker, string, string, string, object?, CancellationToken, object?>)
                Delegate.CreateDelegate(typeof(Func<IServiceInvoker, string, string, string, object?, CancellationToken, object?>), helperMethod);
        }
        else
        {
            throw new NotSupportedException($"Return type '{returnType.Name}' is not supported on Centra service client interface.");
        }

        return new CentraDispatchMethodMetadata
        {
            MethodName = methodName,
            HttpVerb = verb,
            CancellationTokenIndex = ctIndex,
            BodyParameterIndex = bodyIndex,
            Invoker = invoker
        };
    }

    internal static object? InvokeTaskGeneric<TReq, TResp>(IServiceInvoker invoker, string appId, string methodName, string verb, object? body, CancellationToken ct)
    {
        var vt = invoker.InvokeMethodAsync<TReq, TResp>(appId, methodName, (TReq)body!, verb, null, ct);
        return vt.AsTask();
    }

    internal static object? InvokeValueTaskGeneric<TReq, TResp>(IServiceInvoker invoker, string appId, string methodName, string verb, object? body, CancellationToken ct)
    {
        return invoker.InvokeMethodAsync<TReq, TResp>(appId, methodName, (TReq)body!, verb, null, ct);
    }

    internal static object? InvokeTaskVoid<TReq>(IServiceInvoker invoker, string appId, string methodName, string verb, object? body, CancellationToken ct)
    {
        var vt = invoker.InvokeMethodAsync<TReq, object?>(appId, methodName, (TReq)body!, verb, null, ct);
        return vt.AsTask();
    }

    internal static object? InvokeValueTaskVoid<TReq>(IServiceInvoker invoker, string appId, string methodName, string verb, object? body, CancellationToken ct)
    {
        var vt = invoker.InvokeMethodAsync<TReq, object?>(appId, methodName, (TReq)body!, verb, null, ct);
        return new ValueTask(vt.AsTask());
    }
}
