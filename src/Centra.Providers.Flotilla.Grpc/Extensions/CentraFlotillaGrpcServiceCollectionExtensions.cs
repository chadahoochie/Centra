using Centra.Components;
using Centra.Drivers;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Grpc.Client;
using Centra.Providers.Flotilla.Grpc.Options;
using Centra.Providers.Flotilla.PubSub;
using Centra.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Centra.Providers.Flotilla.Grpc.Extensions;

/// <summary>
/// Dependency injection extension methods for registering the Flotilla gRPC consensus pub/sub provider.
/// </summary>
public static class CentraFlotillaGrpcServiceCollectionExtensions
{
    /// <summary>
    /// Adds Centra Flotilla gRPC consensus pub/sub services to the service collection.
    /// </summary>
    public static IServiceCollection AddCentraFlotillaGrpc(
        this IServiceCollection services,
        Action<FlotillaGrpcOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<FlotillaGrpcOptions>();
        }

        services.TryAddSingleton<IFlotillaClient, FlotillaGrpcClient>();
        services.TryAddSingleton<FlotillaGrpcClient>();
        services.TryAddSingleton<FlotillaPubSubDriver>();
        services.AddSingleton<IPubSubDriver>(sp => sp.GetRequiredService<FlotillaPubSubDriver>());
        services.AddSingleton<IPubSubShutdownDrain>(sp => sp.GetRequiredService<FlotillaPubSubDriver>());
        services.AddSingleton<IComponentInitializer, FlotillaGrpcComponentInitializer>();
        services.AddHostedService<FlotillaSubscriptionWorker>();

        return services;
    }

    /// <summary>
    /// Adds a named Centra Flotilla gRPC pub/sub instance to the service collection.
    /// </summary>
    public static IServiceCollection AddCentraFlotillaGrpcPubSub(
        this IServiceCollection services,
        string pubSubName,
        Action<FlotillaGrpcOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);

        services.AddCentraFlotillaGrpc(configure);

        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<FlotillaPubSubDriver>();
            return new FlotillaGrpcDelegateComponentInitializer(registry =>
            {
                registry.RegisterPubSubDriver(pubSubName, driver);
                registry.RegisterComponent(new ComponentDefinition
                {
                    Name = pubSubName,
                    Type = ComponentType.PubSub,
                    Provider = "flotilla-grpc"
                });
            });
        });

        return services;
    }
}
