using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.PubSub;
using Centra.Providers.Flotilla.Udp.Client;
using Centra.Providers.Flotilla.Udp.Options;
using Centra.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Providers.Flotilla.Udp.Extensions;

/// <summary>
/// Dependency injection extension methods for registering the Flotilla UDP consensus pub/sub provider.
/// </summary>
public static class CentraFlotillaUdpServiceCollectionExtensions
{
    /// <summary>
    /// Adds Centra Flotilla UDP consensus pub/sub services to the service collection.
    /// </summary>
    public static IServiceCollection AddCentraFlotillaUdp(
        this IServiceCollection services,
        Action<FlotillaUdpOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<FlotillaUdpOptions>();
        }

        services.TryAddSingleton<IFlotillaClient, FlotillaUdpClient>();
        services.TryAddSingleton<FlotillaUdpClient>();
        services.TryAddSingleton<FlotillaPubSubDriver>();
        services.AddSingleton<IPubSubDriver>(sp => sp.GetRequiredService<FlotillaPubSubDriver>());
        services.AddSingleton<IPubSubShutdownDrain>(sp => sp.GetRequiredService<FlotillaPubSubDriver>());
        services.AddSingleton<IComponentInitializer, FlotillaUdpComponentInitializer>();
        services.AddHostedService<FlotillaSubscriptionWorker>();

        return services;
    }

    /// <summary>
    /// Adds a named Centra Flotilla UDP pub/sub instance to the service collection.
    /// </summary>
    public static IServiceCollection AddCentraFlotillaUdpPubSub(
        this IServiceCollection services,
        string pubSubName,
        Action<FlotillaUdpOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);

        services.AddCentraFlotillaUdp(configure);

        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<FlotillaPubSubDriver>();
            return new FlotillaUdpDelegateComponentInitializer(registry =>
            {
                registry.RegisterPubSubDriver(pubSubName, driver);
                registry.RegisterComponent(new ComponentDefinition
                {
                    Name = pubSubName,
                    Type = ComponentType.PubSub,
                    Provider = "flotilla-udp"
                });
            });
        });

        return services;
    }
}
