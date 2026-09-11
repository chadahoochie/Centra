using System.Reflection;
using Centra.Hosting.Options;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Centra.Registry;
using Centra.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
}
