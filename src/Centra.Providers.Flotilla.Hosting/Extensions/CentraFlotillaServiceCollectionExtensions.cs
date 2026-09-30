using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Options;
using Centra.Providers.Flotilla.PubSub;
using Centra.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Providers.Flotilla.Hosting.Extensions;

/// <summary>
/// Dependency injection extension methods for registering the Flotilla consensus pub/sub provider.
/// </summary>
public static class CentraFlotillaServiceCollectionExtensions
{
    /// <summary>
    /// Adds Centra Flotilla consensus pub/sub services to the service collection.
    /// </summary>
    public static IServiceCollection AddCentraFlotilla(
        this IServiceCollection services,
        Action<FlotillaProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<FlotillaProviderOptions>();
        }

        services.TryAddSingleton<IFlotillaClient, FlotillaUdpClient>();
        services.TryAddSingleton<FlotillaPubSubDriver>();
        services.AddSingleton<IPubSubDriver>(sp => sp.GetRequiredService<FlotillaPubSubDriver>());
        services.AddSingleton<IPubSubShutdownDrain>(sp => sp.GetRequiredService<FlotillaPubSubDriver>());
        services.AddSingleton<IComponentInitializer, FlotillaComponentInitializer>();
        services.AddHostedService<FlotillaSubscriptionWorker>();

        return services;
    }

    /// <summary>
    /// Adds a named Centra Flotilla pub/sub instance to the service collection.
    /// </summary>
    public static IServiceCollection AddCentraFlotillaPubSub(
        this IServiceCollection services,
        string pubSubName,
        Action<FlotillaProviderOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);

        services.AddCentraFlotilla(configure);

        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<FlotillaPubSubDriver>();
            return new FlotillaDelegateComponentInitializer(registry =>
            {
                registry.RegisterPubSubDriver(pubSubName, driver);
                registry.RegisterComponent(new ComponentDefinition
                {
                    Name = pubSubName,
                    Type = ComponentType.PubSub,
                    Provider = "flotilla"
                });
            });
        });

        return services;
    }
}
