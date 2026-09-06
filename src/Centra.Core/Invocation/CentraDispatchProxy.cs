using System.Reflection;
using Centra.Invocation;

namespace Centra.Invocation;

public class CentraDispatchProxy : DispatchProxy
{
    private IServiceInvoker _invoker = null!;
    private string _appId = null!;

    public void Initialize(IServiceInvoker invoker, string appId)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _appId = appId ?? throw new ArgumentNullException(nameof(appId));
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var methodAttr = targetMethod.GetCustomAttribute<ServiceMethodAttribute>();
        var methodName = methodAttr?.Method ?? targetMethod.Name;
        var verb = methodAttr?.HttpVerb ?? "POST";

        var parameters = targetMethod.GetParameters();
        object? requestBody = null;
        var ct = CancellationToken.None;

        if (args is not null)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(CancellationToken))
                {
                    ct = (CancellationToken)(args[i] ?? CancellationToken.None);
                }
                else if (requestBody is null)
                {
                    requestBody = args[i];
                }
            }
        }

        var returnType = targetMethod.ReturnType;

        // Task<T> or ValueTask<T>
        if (returnType.IsGenericType && (returnType.GetGenericTypeDefinition() == typeof(Task<>) || returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            var responseType = returnType.GetGenericArguments()[0];
            var requestType = requestBody?.GetType() ?? typeof(object);

            var invokeMethod = typeof(IServiceInvoker).GetMethod(nameof(IServiceInvoker.InvokeMethodAsync))!
                .MakeGenericMethod(requestType, responseType);

            var valueTaskResult = invokeMethod.Invoke(_invoker, [_appId, methodName, requestBody, verb, null, ct]);

            if (returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                // Convert ValueTask<T> to Task<T> via AsTask()
                var asTaskMethod = valueTaskResult!.GetType().GetMethod(nameof(ValueTask<object>.AsTask))!;
                return asTaskMethod.Invoke(valueTaskResult, null);
            }

            return valueTaskResult;
        }

        if (returnType == typeof(Task))
        {
            var invokeMethod = typeof(IServiceInvoker).GetMethod(nameof(IServiceInvoker.InvokeMethodAsync))!
                .MakeGenericMethod(requestBody?.GetType() ?? typeof(object), typeof(object));

            var vt = (ValueTask<object?>)invokeMethod.Invoke(_invoker, [_appId, methodName, requestBody, verb, null, ct])!;
            return vt.AsTask();
        }

        if (returnType == typeof(ValueTask))
        {
            var invokeMethod = typeof(IServiceInvoker).GetMethod(nameof(IServiceInvoker.InvokeMethodAsync))!
                .MakeGenericMethod(requestBody?.GetType() ?? typeof(object), typeof(object));

            var vt = (ValueTask<object?>)invokeMethod.Invoke(_invoker, [_appId, methodName, requestBody, verb, null, ct])!;
            return new ValueTask(vt.AsTask());
        }

        throw new NotSupportedException($"Return type '{returnType.Name}' is not supported on Centra service client interface.");
    }
}
