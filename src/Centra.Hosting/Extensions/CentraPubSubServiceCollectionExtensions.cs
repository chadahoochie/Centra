using System.Reflection;
using Centra.Events;
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

        services.TryAddSingleton<IRuleFilterEvaluator, Centra.PubSub.Routing.Rules.RuleFilterEvaluator>();

        services.TryAddSingleton<IPubSubClient>(sp =>
        {
            var registry = sp.GetRequiredService<ComponentRegistry>();
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var resilience = sp.GetService<IResiliencePipelineProvider>();
            var offloadCoordinator = sp.GetService<Centra.PubSub.Tenancy.ITenantOffloadCoordinator>();
            return new CentraPubSubClient(registry, options.AppId, options.DefaultPubSub, resilience, offloadCoordinator);
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
        IReadOnlyDictionary<string, object?>? customArguments = null,
        string? ruleFilter = null,
        int? priority = null)
        where THandler : class, IEventHandler<TEvent>
    {
        services.TryAddTransient<THandler>();

        var topicAttrs = typeof(THandler).GetCustomAttributes<TopicAttribute>().ToArray();
        var topicAttr = topicAttrs.FirstOrDefault(a => (topic == null || a.Topic == topic) && (ruleFilter == null || a.RuleFilter == ruleFilter)) ?? topicAttrs.FirstOrDefault();
        var resolvedPubSub = pubSubName ?? topicAttr?.PubSubName ?? "pubsub";
        var resolvedTopic = topic ?? topicAttr?.Topic ?? typeof(TEvent).Name;
        var resolvedDlTopic = deadLetterTopic ?? topicAttr?.DeadLetterTopic;
        var resolvedRuleFilter = ruleFilter ?? topicAttr?.RuleFilter;
        var resolvedPriority = priority ?? topicAttr?.Priority ?? 0;
        var resolvedConsumerMode = consumerMode ?? topicAttr?.ConsumerMode ?? ConsumerMode.CompetingConsumer;
        var resolvedPrefetchCount = prefetchCount ?? (topicAttr?.PrefetchCount > 0 ? topicAttr.PrefetchCount : null);
        var resolvedMaxConcurrentCalls = maxConcurrentCalls ?? (topicAttr?.MaxConcurrentCalls > 0 ? topicAttr.MaxConcurrentCalls : null);
        var resolvedTtl = messageTimeToLive ?? (topicAttr?.MessageTtlSeconds > 0 ? TimeSpan.FromSeconds(topicAttr.MessageTtlSeconds) : null);
        var resolvedAutoDelete = autoDelete ?? topicAttr?.AutoDelete ?? false;

        ICompiledRuleFilter? compiledFilter = null;
        if (!string.IsNullOrWhiteSpace(resolvedRuleFilter))
        {
            var evaluator = new Centra.PubSub.Routing.Rules.RuleFilterEvaluator();
            compiledFilter = evaluator.Compile(resolvedRuleFilter);
        }

        services.ReplaceRegistrationFor<CentraTopicRegistration>(
            r => r.HandlerType == typeof(THandler) &&
                 (r.Topic == resolvedTopic || (topic == null && topicAttr != null && r.Topic == topicAttr.Topic)) &&
                 r.RuleFilter == resolvedRuleFilter,
            new CentraTopicRegistration(
                resolvedPubSub,
                resolvedTopic,
                typeof(TEvent),
                typeof(THandler),
                resolvedDlTopic,
                CentraTopicRegistration.CreateTypedInvoker<TEvent>(),
                resolvedRuleFilter,
                resolvedPriority,
                compiledFilter,
                predicate: null,
                resolvedConsumerMode,
                resolvedPrefetchCount,
                resolvedMaxConcurrentCalls,
                resolvedTtl,
                resolvedAutoDelete,
                customArguments));

        return services;
    }

    public static IServiceCollection AddCentraEventHandler<THandler, TEvent>(
        this IServiceCollection services,
        Func<TEvent, EventContext, bool> predicate,
        string? pubSubName = null,
        string? topic = null,
        string? deadLetterTopic = null,
        int? priority = null,
        ConsumerMode? consumerMode = null,
        int? prefetchCount = null,
        int? maxConcurrentCalls = null,
        TimeSpan? messageTimeToLive = null,
        bool? autoDelete = null,
        IReadOnlyDictionary<string, object?>? customArguments = null)
        where THandler : class, IEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(predicate);
        services.TryAddTransient<THandler>();

        var topicAttrs = typeof(THandler).GetCustomAttributes<TopicAttribute>().ToArray();
        var topicAttr = topicAttrs.FirstOrDefault(a => topic == null || a.Topic == topic) ?? topicAttrs.FirstOrDefault();
        var resolvedPubSub = pubSubName ?? topicAttr?.PubSubName ?? "pubsub";
        var resolvedTopic = topic ?? topicAttr?.Topic ?? typeof(TEvent).Name;
        var resolvedDlTopic = deadLetterTopic ?? topicAttr?.DeadLetterTopic;
        var resolvedPriority = priority ?? topicAttr?.Priority ?? 0;
        var resolvedConsumerMode = consumerMode ?? topicAttr?.ConsumerMode ?? ConsumerMode.CompetingConsumer;
        var resolvedPrefetchCount = prefetchCount ?? (topicAttr?.PrefetchCount > 0 ? topicAttr.PrefetchCount : null);
        var resolvedMaxConcurrentCalls = maxConcurrentCalls ?? (topicAttr?.MaxConcurrentCalls > 0 ? topicAttr.MaxConcurrentCalls : null);
        var resolvedTtl = messageTimeToLive ?? (topicAttr?.MessageTtlSeconds > 0 ? TimeSpan.FromSeconds(topicAttr.MessageTtlSeconds) : null);
        var resolvedAutoDelete = autoDelete ?? topicAttr?.AutoDelete ?? false;

        Func<EventContext, ReadOnlyMemory<byte>, bool> untypedPredicate = (ctx, payload) =>
        {
            var unpacked = Centra.Events.CloudEventUnpacker.Unpack<TEvent>(payload, ctx.Headers);
            if (unpacked.Data is null)
            {
                return false;
            }
            return predicate(unpacked.Data, ctx);
        };

        services.ReplaceRegistrationFor<CentraTopicRegistration>(
            r => r.HandlerType == typeof(THandler) &&
                 (r.Topic == resolvedTopic || (topic == null && topicAttr != null && r.Topic == topicAttr.Topic)) &&
                 r.Predicate != null,
            new CentraTopicRegistration(
                resolvedPubSub,
                resolvedTopic,
                typeof(TEvent),
                typeof(THandler),
                resolvedDlTopic,
                CentraTopicRegistration.CreateTypedInvoker<TEvent>(),
                ruleFilter: null,
                resolvedPriority,
                compiledFilter: null,
                predicate: untypedPredicate,
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
