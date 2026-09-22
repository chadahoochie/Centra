using Centra;
using Centra.Components;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

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

        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<CentraOptions>>().Value);

        // Component Registry
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

        return services;
    }
}
