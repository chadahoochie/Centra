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
        services.AddOptions<CentraOptions>().Configure<IServiceProvider>((opts, sp) =>
        {
            var config = sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            if (config is not null)
            {
                Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(config.GetSection("Centra"), opts);
            }
        });

        if (configure is not null)
        {
            services.Configure(configure);
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
