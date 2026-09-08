using System.Collections.Concurrent;
using System.Reflection;
using Centra.Invocation;

namespace Centra.Invocation;

public static class ServiceProxyFactory
{
    private static readonly ConcurrentDictionary<Type, string> AppIdCache = new();

    public static TInterface Create<TInterface>(IServiceInvoker invoker) where TInterface : class
    {
        ArgumentNullException.ThrowIfNull(invoker);

        if (!typeof(TInterface).IsInterface)
        {
            throw new InvalidOperationException($"Type '{typeof(TInterface).Name}' must be an interface to create a Centra service proxy.");
        }

        var appId = AppIdCache.GetOrAdd(typeof(TInterface), static type =>
        {
            var attr = type.GetCustomAttribute<ServiceClientAttribute>();
            if (attr is null || string.IsNullOrWhiteSpace(attr.AppId))
            {
                throw new InvalidOperationException($"Interface '{type.Name}' must be decorated with [ServiceClient(\"app-id\")]");
            }
            return attr.AppId;
        });

        var proxy = DispatchProxy.Create<TInterface, CentraDispatchProxy>();
        ((CentraDispatchProxy)(object)proxy).Initialize(invoker, appId);

        return proxy;
    }
}
