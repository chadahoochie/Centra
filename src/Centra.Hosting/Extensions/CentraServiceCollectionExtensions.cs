using System.Reflection;
using Centra.Bindings;
using Centra.Components;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Options;
using Centra.Hosting.Routing;
using Centra.Invocation;
using Centra.Locks;
using Centra.PubSub;
using Centra.Registry;
using Centra.Serialization;
using Centra.State;
using Centra.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.Extensions;

public static class CentraServiceCollectionExtensions
{
    public static IServiceCollection AddCentra(
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

        // State Store
        services.TryAddSingleton<IStateStore, CentraStateStore>();
        services.TryAddTransient(typeof(IStateStore<>), typeof(CentraStateStoreRegistrationHelper<>));

        // Pub/Sub Client
        services.TryAddSingleton<IPubSubClient>(sp =>
        {
            var registry = sp.GetRequiredService<ComponentRegistry>();
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            return new CentraPubSubClient(registry, options.AppId, options.DefaultPubSub);
        });

        // Distributed Lock Provider
        services.TryAddSingleton<IDistributedLockProvider, CentraDistributedLockProvider>();

        // Output Bindings
        services.TryAddSingleton<IOutputBinding, CentraOutputBinding>();

        // Service Invocation
        services.AddHttpClient();
        services.TryAddSingleton<IServiceInvoker, CentraServiceInvoker>();

        // Control Plane Client & Sync Service
        services.AddHttpClient<IControlPlaneClient, ControlPlaneClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var endpoint = options.ControlPlaneEndpoint ?? options.ControlPlane.Endpoint;
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                http.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
            }
        });
        services.AddHostedService<CentraControlPlaneSyncHostedService>();

        // Runtime Hosted Service
        services.AddHostedService<CentraRuntimeHostedService>();

        return services;
    }

    public static IServiceCollection AddCentraServiceClient<TInterface>(this IServiceCollection services)
        where TInterface : class
    {
        services.TryAddTransient<TInterface>(sp =>
        {
            var invoker = sp.GetRequiredService<IServiceInvoker>();
            return ServiceProxyFactory.Create<TInterface>(invoker);
        });

        return services;
    }

    public static IServiceCollection AddCentraStateStore<T>(this IServiceCollection services, string storeName)
    {
        services.TryAddTransient<IStateStore<T>>(sp =>
        {
            var stateStore = sp.GetRequiredService<IStateStore>();
            return new CentraStateStore<T>(stateStore, storeName);
        });

        return services;
    }

    public static IServiceCollection AddCentraEventHandler<THandler, TEvent>(
        this IServiceCollection services,
        string? pubSubName = null,
        string? topic = null,
        string? deadLetterTopic = null)
        where THandler : class, IEventHandler<TEvent>
    {
        services.TryAddTransient<THandler>();

        var topicAttr = typeof(THandler).GetCustomAttribute<TopicAttribute>();
        var resolvedPubSub = pubSubName ?? topicAttr?.PubSubName ?? "pubsub";
        var resolvedTopic = topic ?? topicAttr?.Topic ?? typeof(TEvent).Name;
        var resolvedDlTopic = deadLetterTopic ?? topicAttr?.DeadLetterTopic;

        services.AddSingleton(new CentraTopicRegistration(
            resolvedPubSub,
            resolvedTopic,
            typeof(TEvent),
            typeof(THandler),
            resolvedDlTopic));

        return services;
    }
}
