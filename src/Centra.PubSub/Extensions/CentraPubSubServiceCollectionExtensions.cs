using System.Reflection;
using Centra;
using Centra.Events;
using Centra.PubSub;
using Centra.PubSub.HostedServices;
using Centra.PubSub.Inbox;
using Centra.PubSub.Outbox;
using Centra.PubSub.Routing;
using Centra.Registry;
using Centra.Resilience;
using Centra.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class CentraPubSubServiceCollectionExtensions
{
    public static IServiceCollection TryAddCentraPubSubSerializerRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton<IPubSubSerializerRegistry>(sp =>
        {
            var registry = new CentraPubSubSerializerRegistry();
            var registrations = sp.GetServices<PubSubSerializerRegistration>();
            foreach (var r in registrations)
            {
                var serializer = r.SerializerInstance ?? (ICentraSerializer)sp.GetRequiredService(r.SerializerType!);
                var ct = !string.IsNullOrWhiteSpace(r.ContentType) ? r.ContentType : serializer.ContentType;
                if (!string.IsNullOrWhiteSpace(ct))
                {
                    registry.Register(ct, serializer);
                }
            }
            return registry;
        });

        return services;
    }

    public static IServiceCollection AddCentraPubSub(
        this IServiceCollection services,
        Action<CentraOptions>? configure = null)
    {
        services.AddCentraCore(configure);

        services.TryAddCentraPubSubSerializerRegistry();

        services.TryAddSingleton<IRuleFilterEvaluator, Centra.PubSub.Routing.Rules.RuleFilterEvaluator>();

        services.TryAddSingleton<IPubSubClient>(sp =>
        {
            var registry = sp.GetRequiredService<ComponentRegistry>();
            var options = sp.GetRequiredService<IOptions<CentraOptions>>().Value;
            var resilience = sp.GetService<IResiliencePipelineProvider>();
            var offloadCoordinator = sp.GetService<Centra.PubSub.Tenancy.ITenantOffloadCoordinator>();
            var serializer = sp.GetService<IPubSubSerializer>() ?? sp.GetService<ICentraSerializer>();
            var serializerRegistry = sp.GetService<IPubSubSerializerRegistry>();
            return new CentraPubSubClient(registry, options.AppId, options.DefaultPubSub, resilience, offloadCoordinator, serializer, serializerRegistry);
        });

        services.AddHostedService<CentraRuntimeHostedService>();

        return services;
    }

    public static IServiceCollection AddCentraPubSubSerializer<TSerializer>(
        this IServiceCollection services,
        string? contentType = null)
        where TSerializer : class, ICentraSerializer
    {
        services.TryAddCentraPubSubSerializerRegistry();

        services.TryAddSingleton<TSerializer>();
        if (typeof(IPubSubSerializer).IsAssignableFrom(typeof(TSerializer)))
        {
            services.AddSingleton<IPubSubSerializer>(sp => (IPubSubSerializer)sp.GetRequiredService<TSerializer>());
        }
        else
        {
            services.AddSingleton<IPubSubSerializer>(sp => new PubSubSerializerAdapter(sp.GetRequiredService<TSerializer>()));
        }

        services.AddSingleton(new PubSubSerializerRegistration(contentType ?? string.Empty, typeof(TSerializer), null));

        return services;
    }

    public static IServiceCollection AddCentraPubSubSerializer(
        this IServiceCollection services,
        ICentraSerializer serializer,
        string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        services.TryAddCentraPubSubSerializerRegistry();

        if (serializer is IPubSubSerializer pubSubSerializer)
        {
            services.AddSingleton(pubSubSerializer);
        }
        else
        {
            services.AddSingleton<IPubSubSerializer>(new PubSubSerializerAdapter(serializer));
        }

        var ct = !string.IsNullOrWhiteSpace(contentType) ? contentType : serializer.ContentType;
        services.AddSingleton(new PubSubSerializerRegistration(ct, null, serializer));

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
        int? priority = null,
        Type? serializerType = null,
        ICentraSerializer? serializer = null,
        string? contentType = null)
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
        var resolvedSerializerType = serializerType ?? topicAttr?.SerializerType;
        var resolvedContentType = contentType ?? topicAttr?.ContentType;

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
                invoker: null,
                resolvedRuleFilter,
                resolvedPriority,
                compiledFilter,
                predicate: null,
                resolvedConsumerMode,
                resolvedPrefetchCount,
                resolvedMaxConcurrentCalls,
                resolvedTtl,
                resolvedAutoDelete,
                customArguments,
                resolvedSerializerType,
                serializer,
                resolvedContentType));

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
        IReadOnlyDictionary<string, object?>? customArguments = null,
        Type? serializerType = null,
        ICentraSerializer? serializer = null,
        string? contentType = null)
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
        var resolvedSerializerType = serializerType ?? topicAttr?.SerializerType;
        var resolvedContentType = contentType ?? topicAttr?.ContentType;

        Func<EventContext, ReadOnlyMemory<byte>, bool> untypedPredicate = (ctx, payload) =>
        {
            var unpacked = Centra.Events.CloudEventUnpacker.Unpack<TEvent>(payload, ctx.Headers, serializer);
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
                invoker: null,
                ruleFilter: null,
                resolvedPriority,
                compiledFilter: null,
                predicate: untypedPredicate,
                resolvedConsumerMode,
                resolvedPrefetchCount,
                resolvedMaxConcurrentCalls,
                resolvedTtl,
                resolvedAutoDelete,
                customArguments,
                resolvedSerializerType,
                serializer,
                resolvedContentType));

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
            var serializer = sp.GetService<IPubSubSerializer>() ?? sp.GetService<ICentraSerializer>();
            var serializerRegistry = sp.GetService<IPubSubSerializerRegistry>();
            return new CentraOutboxPublisher(store, defaultPubSub, options.AppId, serializer, serializerRegistry);
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
}
