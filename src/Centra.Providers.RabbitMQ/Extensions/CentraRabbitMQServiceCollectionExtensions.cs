using Centra.Drivers;
using Centra.Providers.RabbitMQ.Options;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.Extensions;

public static class CentraRabbitMQServiceCollectionExtensions
{
    public static IServiceCollection AddCentraRabbitMQ(
        this IServiceCollection services,
        Action<RabbitMQProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<RabbitMQProviderOptions>();
        }

        services.TryAddSingleton<IConnectionFactory>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RabbitMQProviderOptions>>().Value;
            return new ConnectionFactory
            {
                HostName = opts.HostName,
                Port = opts.Port,
                UserName = opts.UserName,
                Password = opts.Password,
                VirtualHost = opts.VirtualHost
            };
        });

        services.TryAddSingleton<RabbitMQPubSubDriver>();
        services.AddSingleton<IPubSubDriver>(sp => sp.GetRequiredService<RabbitMQPubSubDriver>());
        services.AddSingleton<IComponentInitializer, RabbitMQComponentInitializer>();

        return services;
    }

    public static IServiceCollection AddCentraRabbitMQPubSub(
        this IServiceCollection services,
        string pubSubName,
        Action<RabbitMQProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<RabbitMQProviderOptions>();
        }

        services.TryAddSingleton<IConnectionFactory>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RabbitMQProviderOptions>>().Value;
            return new ConnectionFactory
            {
                HostName = opts.HostName,
                Port = opts.Port,
                UserName = opts.UserName,
                Password = opts.Password,
                VirtualHost = opts.VirtualHost
            };
        });

        services.TryAddSingleton<RabbitMQPubSubDriver>();
        services.AddSingleton<IComponentInitializer>(sp =>
        {
            var driver = sp.GetRequiredService<RabbitMQPubSubDriver>();
            return new RabbitMQDelegateComponentInitializer(reg =>
            {
                reg.RegisterPubSubDriver(pubSubName, driver);
                reg.RegisterComponent(new Components.ComponentDefinition
                {
                    Name = pubSubName,
                    Type = Components.ComponentType.PubSub,
                    Provider = "rabbitmq"
                });
            });
        });

        return services;
    }
}
