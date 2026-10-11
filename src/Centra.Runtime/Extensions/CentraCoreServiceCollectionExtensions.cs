using Centra;
using Centra.Components;
using Centra.Locks;
using Centra.PubSub;
using Centra.Registry;
using Centra.State;
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

        // Subsystem options synchronized with root CentraOptions
        services.AddOptions<CentraControlPlaneOptions>().Configure<IOptions<CentraOptions>>((opts, root) =>
        {
            opts.Endpoint ??= root.Value.ControlPlane.Endpoint;
            opts.ClusterId = root.Value.ControlPlane.ClusterId;
            opts.HeartbeatInterval = root.Value.ControlPlane.HeartbeatInterval;
            opts.EnableLiveSync = root.Value.ControlPlane.EnableLiveSync;
            opts.ClusterToken ??= root.Value.ControlPlane.ClusterToken;
            opts.UseHmacAuthentication = root.Value.ControlPlane.UseHmacAuthentication;
        });
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<CentraOptions>>().Value.ControlPlane);

        services.AddOptions<CentraStateOptions>().Configure<IOptions<CentraOptions>>((opts, root) =>
        {
            opts.DefaultStore = root.Value.State.DefaultStore;
        });
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<CentraOptions>>().Value.State);

        services.AddOptions<CentraPubSubOptions>().Configure<IOptions<CentraOptions>>((opts, root) =>
        {
            opts.DefaultStore = root.Value.PubSub.DefaultStore;
        });
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<CentraOptions>>().Value.PubSub);

        services.AddOptions<CentraLocksOptions>().Configure<IOptions<CentraOptions>>((opts, root) =>
        {
            opts.DefaultStore = root.Value.Locks.DefaultStore;
            opts.DefaultExpiry = root.Value.Locks.DefaultExpiry;
            opts.DefaultTimeout = root.Value.Locks.DefaultTimeout;
        });
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<CentraOptions>>().Value.Locks);

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
