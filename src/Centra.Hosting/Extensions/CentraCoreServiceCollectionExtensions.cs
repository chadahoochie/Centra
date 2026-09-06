using Centra.Components;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Options;
using Centra.Registry;
using Centra.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Centra.Hosting.Extensions;

public static class CentraCoreServiceCollectionExtensions
{
    public static IServiceCollection AddCentraCore(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<CentraOptions>();
        }

        // Component Registry & Serializer
        services.TryAddSingleton<ComponentRegistry>(sp =>
        {
            var registry = new ComponentRegistry();
            var initializers = sp.GetServices<IComponentInitializer>();
            foreach (var init in initializers)
            {
                init.Initialize(registry);
            }
            return registry;
        });

        services.TryAddSingleton<IComponentRegistry>(sp => sp.GetRequiredService<ComponentRegistry>());
        services.TryAddSingleton<ICentraSerializer, JsonCentraSerializer>();

        // Runtime Hosted Service
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CentraRuntimeHostedService>());

        return services;
    }
}
