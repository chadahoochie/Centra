using Centra.PubSub;

namespace Centra.Hosting.Routing;

internal static class CentraTopicSubscriptionOptionsResolver
{
    public static (PubSubSubscribeOptions Options, string? DeadLetterTopic) Resolve(IReadOnlyList<CentraTopicRegistration> registrations)
    {
        if (registrations.Count == 0)
        {
            return (new PubSubSubscribeOptions(), null);
        }

        var consumerMode = ConsumerMode.CompetingConsumer;
        int? prefetchCount = null;
        int? maxConcurrentCalls = null;
        TimeSpan? ttl = null;
        var autoDelete = true;
        string? deadLetterTopic = null;
        Dictionary<string, object?>? customArgs = null;

        for (var i = 0; i < registrations.Count; i++)
        {
            var reg = registrations[i];

            if (reg.ConsumerMode == ConsumerMode.SingleActiveConsumer)
            {
                consumerMode = ConsumerMode.SingleActiveConsumer;
            }

            if (reg.PrefetchCount.HasValue)
            {
                prefetchCount = Math.Max(prefetchCount ?? 0, reg.PrefetchCount.Value);
            }

            if (reg.MaxConcurrentCalls.HasValue)
            {
                maxConcurrentCalls = Math.Max(maxConcurrentCalls ?? 0, reg.MaxConcurrentCalls.Value);
            }

            if (reg.MessageTimeToLive.HasValue)
            {
                ttl = ttl.HasValue ? (reg.MessageTimeToLive.Value < ttl.Value ? reg.MessageTimeToLive.Value : ttl.Value) : reg.MessageTimeToLive.Value;
            }

            if (!reg.AutoDelete)
            {
                autoDelete = false;
            }

            if (deadLetterTopic is null && !string.IsNullOrWhiteSpace(reg.DeadLetterTopic))
            {
                deadLetterTopic = reg.DeadLetterTopic;
            }

            if (reg.CustomArguments is not null)
            {
                customArgs ??= new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (k, v) in reg.CustomArguments)
                {
                    customArgs[k] = v;
                }
            }
        }

        var options = new PubSubSubscribeOptions
        {
            ConsumerMode = consumerMode,
            PrefetchCount = prefetchCount,
            MaxConcurrentCalls = maxConcurrentCalls,
            MessageTimeToLive = ttl,
            AutoDelete = autoDelete,
            CustomArguments = customArgs
        };

        return (options, deadLetterTopic);
    }
}
