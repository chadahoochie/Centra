using System.Reflection;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Inbox;
using Centra.Hosting.Options;
using Centra.Hosting.Outbox;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Centra.PubSub.Inbox;
using Centra.PubSub.Outbox;
using Centra.Registry;
using Centra.Resilience;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.Extensions;

public static class CentraPubSubServiceCollectionExtensions
{
    public static IServiceCollection AddCentraPubSub(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        services.AddCentraCore(configure);

        services.TryAddSingleton<IPubSubClient>(sp =>
        {
            var registry = sp.GetRequiredService<ComponentRegistry>();
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var resilience = sp.GetService<IResiliencePipelineProvider>();
            return new CentraPubSubClient(registry, options.AppId, options.DefaultPubSub, resilience);
        });

        return services;
    }

    public static IServiceCollection AddCentraEventHandler<THandler, TEvent>(
        this IServiceCollection services,
        string? pubSubName = null,
        string? topic = null,
        string? deadLetterTopic = null,
        ConsumerMode? consumerMode = null,
        int? prefetchCount = null,
        int? maxConcurrentCalls = null,
        TimeSpan? messageTimeToLive = null,
        bool? autoDelete = null,
        IReadOnlyDictionary<string, object?>? customArguments = null)
        where THandler : class, IEventHandler<TEvent>
    {
        services.TryAddTransient<THandler>();

        var topicAttr = typeof(THandler).GetCustomAttribute<TopicAttribute>();
        var resolvedPubSub = pubSubName ?? topicAttr?.PubSubName ?? "pubsub";
        var resolvedTopic = topic ?? topicAttr?.Topic ?? typeof(TEvent).Name;
        var resolvedDlTopic = deadLetterTopic ?? topicAttr?.DeadLetterTopic;
        var resolvedConsumerMode = consumerMode ?? topicAttr?.ConsumerMode ?? ConsumerMode.CompetingConsumer;
        var resolvedPrefetchCount = prefetchCount ?? (topicAttr?.PrefetchCount > 0 ? topicAttr.PrefetchCount : null);
        var resolvedMaxConcurrentCalls = maxConcurrentCalls ?? (topicAttr?.MaxConcurrentCalls > 0 ? topicAttr.MaxConcurrentCalls : null);
        var resolvedTtl = messageTimeToLive ?? (topicAttr?.MessageTtlSeconds > 0 ? TimeSpan.FromSeconds(topicAttr.MessageTtlSeconds) : null);
        var resolvedAutoDelete = autoDelete ?? topicAttr?.AutoDelete ?? false;

        services.ReplaceRegistrationFor<CentraTopicRegistration>(
            r => r.HandlerType == typeof(THandler),
            new CentraTopicRegistration(
                resolvedPubSub,
                resolvedTopic,
                typeof(TEvent),
                typeof(THandler),
                resolvedDlTopic,
                CentraTopicRegistration.CreateTypedInvoker<TEvent>(),
                resolvedConsumerMode,
                resolvedPrefetchCount,
                resolvedMaxConcurrentCalls,
                resolvedTtl,
                resolvedAutoDelete,
                customArguments));

        return services;
    }

    public static IServiceCollection AddCentraOutbox(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null)
    {
        services.AddCentraPubSub();

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new OutboxOptions()));
        }

        services.TryAddSingleton<IOutboxStore, InMemoryOutboxStore>();

        services.TryAddSingleton<IOutboxPublisher>(sp =>
        {
            var store = sp.GetRequiredService<IOutboxStore>();
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var outboxOpts = sp.GetService<IOptions<OutboxOptions>>()?.Value;
            var defaultPubSub = outboxOpts?.DefaultPubSubName ?? options.DefaultPubSub;
            return new CentraOutboxPublisher(store, defaultPubSub, options.AppId);
        });

        services.TryAddSingleton<OutboxProcessor>(sp =>
        {
            var store = sp.GetRequiredService<IOutboxStore>();
            var registry = sp.GetRequiredService<ComponentRegistry>();
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var resilience = sp.GetService<IResiliencePipelineProvider>();
            var defaultPubSub = sp.GetService<IOptions<OutboxOptions>>()?.Value.DefaultPubSubName ?? options.DefaultPubSub;
            var driver = registry.GetPubSubDriver(defaultPubSub) 
                ?? throw new InvalidOperationException($"No PubSub driver registered for outbox pubsub '{defaultPubSub}'");
            var outboxOpts = sp.GetService<IOptions<OutboxOptions>>()?.Value;
            var logger = sp.GetService<ILogger<OutboxProcessor>>();
            return new OutboxProcessor(store, driver, outboxOpts, logger);
        });

        services.AddHostedService<CentraOutboxHostedService>();

        return services;
    }

    public static IServiceCollection AddCentraStateStoreOutbox(
        this IServiceCollection services,
        string? stateStoreName = null,
        Action<OutboxOptions>? configure = null)
    {
        services.AddCentraOutbox(configure);

        services.Replace(ServiceDescriptor.Singleton<IOutboxStore>(sp =>
        {
            var stateStore = sp.GetRequiredService<IStateStore>();
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var resolvedStore = stateStoreName ?? options.DefaultStateStore;
            return new StateStoreOutboxStore(stateStore, resolvedStore);
        }));

        return services;
    }

    public static IServiceCollection AddCentraInbox(
        this IServiceCollection services,
        Action<InboxOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new InboxOptions()));
        }

        services.TryAddSingleton<IInboxStore, InMemoryInboxStore>();
        return services;
    }

    public static IServiceCollection AddCentraStateStoreInbox(
        this IServiceCollection services,
        string? stateStoreName = null,
        Action<InboxOptions>? configure = null)
    {
        services.AddCentraInbox(configure);

        services.Replace(ServiceDescriptor.Singleton<IInboxStore>(sp =>
        {
            var stateStore = sp.GetRequiredService<IStateStore>();
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var resolvedStore = stateStoreName ?? options.DefaultStateStore;
            return new StateStoreInboxStore(stateStore, resolvedStore);
        }));

        return services;
    }
}
