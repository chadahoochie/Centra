using Centra.Hosting.Options;
using Centra.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Hosting.Extensions;

public static class CentraInvocationServiceCollectionExtensions
{
    public static IServiceCollection AddCentraInvocation(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        services.AddCentraCore(configure);

        services.AddHttpClient();
        services.TryAddSingleton<IServiceInvoker, CentraServiceInvoker>();

        return services;
    }

    public static IServiceCollection AddCentraServiceClient<TInterface>(this IServiceCollection services)
        where TInterface : class
    {
        services.TryAddTransient<TInterface>(sp =>
        {
            var invoker = sp.GetRequiredService<IServiceInvoker>();
            return ServiceProxyFactory.Create<TInterface>(invoker);
        });

        return services;
    }
}
