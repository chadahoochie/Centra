using Azure.Messaging.ServiceBus;
using Centra.Drivers;
using Centra.Providers.AzureServiceBus.Options;
using Centra.Providers.AzureServiceBus.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Centra.Providers.AzureServiceBus.Extensions;

public static class CentraAzureServiceBusServiceCollectionExtensions
{
    public static IServiceCollection AddCentraAzureServiceBus(
        this IServiceCollection services,
        Action<AzureServiceBusProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<AzureServiceBusProviderOptions>();
        }

        services.TryAddSingleton<ServiceBusClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureServiceBusProviderOptions>>().Value;
            return new ServiceBusClient(opts.ConnectionString);
        });

        services.TryAddSingleton<AzureServiceBusPubSubDriver>();
        services.AddSingleton<IPubSubDriver>(sp => sp.GetRequiredService<AzureServiceBusPubSubDriver>());
        services.AddSingleton<IComponentInitializer, AzureServiceBusComponentInitializer>();

        return services;
    }

    public static IServiceCollection AddCentraAzureServiceBusPubSub(
        this IServiceCollection services,
        string pubSubName,
        Action<AzureServiceBusProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<AzureServiceBusProviderOptions>();
        }

        services.TryAddSingleton<ServiceBusClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureServiceBusProviderOptions>>().Value;
            return new ServiceBusClient(opts.ConnectionString);
        });

        services.TryAddSingleton<AzureServiceBusPubSubDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<AzureServiceBusPubSubDriver>();
            return new AzureServiceBusDelegateComponentInitializer(reg =>
            {
                reg.RegisterPubSubDriver(pubSubName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = pubSubName,
                    Type = Components.ComponentType.PubSub,
                    Provider = "azureservicebus"
                });
            });
        });

        return services;
    }
}
