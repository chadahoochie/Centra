using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.PubSub;
using Centra.Providers.Flotilla.Tcp.Client;
using Centra.Providers.Flotilla.Tcp.Options;
using Centra.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Providers.Flotilla.Tcp.Extensions;

/// <summary>
/// Dependency injection extension methods for registering the Flotilla TCP consensus pub/sub provider.
/// </summary>
public static class CentraFlotillaTcpServiceCollectionExtensions
{
    /// <summary>
    /// Adds Centra Flotilla TCP consensus pub/sub services to the service collection.
    /// </summary>
    public static IServiceCollection AddCentraFlotillaTcp(
        this IServiceCollection services,
        Action<FlotillaTcpOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<FlotillaTcpOptions>();
        }

        services.TryAddSingleton<IFlotillaClient, FlotillaTcpClient>();
        services.TryAddSingleton<FlotillaTcpClient>();
        services.TryAddSingleton<FlotillaPubSubDriver>();
        services.AddSingleton<IPubSubDriver>(sp => sp.GetRequiredService<FlotillaPubSubDriver>());
        services.AddSingleton<IPubSubShutdownDrain>(sp => sp.GetRequiredService<FlotillaPubSubDriver>());
        services.AddSingleton<IComponentInitializer, FlotillaTcpComponentInitializer>();
        services.AddHostedService<FlotillaSubscriptionWorker>();

        return services;
    }

    /// <summary>
    /// Adds a named Centra Flotilla TCP pub/sub instance to the service collection.
    /// </summary>
    public static IServiceCollection AddCentraFlotillaTcpPubSub(
        this IServiceCollection services,
        string pubSubName,
        Action<FlotillaTcpOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);

        services.AddCentraFlotillaTcp(configure);

        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<FlotillaPubSubDriver>();
            return new FlotillaTcpDelegateComponentInitializer(registry =>
            {
                registry.RegisterPubSubDriver(pubSubName, driver);
                registry.RegisterComponent(new ComponentDefinition
                {
                    Name = pubSubName,
                    Type = ComponentType.PubSub,
                    Provider = "flotilla-tcp"
                });
            });
        });

        return services;
    }
}
