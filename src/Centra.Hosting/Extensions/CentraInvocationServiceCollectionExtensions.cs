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
        services.TryAddSingleton<IServiceEndpointResolver>(sp =>
        {
            var topologyProvider = sp.GetService<Centra.Sync.IClusterTopologyProvider>();
            IServiceEndpointResolver baseResolver = topologyProvider is not null
                ? new ControlPlaneServiceEndpointResolver(
                    topologyProvider,
                    sp.GetService<Microsoft.Extensions.Logging.ILogger<ControlPlaneServiceEndpointResolver>>())
                : PassThroughServiceEndpointResolver.Instance;

            var configuration = sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            return configuration is not null
                ? new Centra.Hosting.Invocation.ConfigurationServiceEndpointResolver(configuration, baseResolver)
                : baseResolver;
        });
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
