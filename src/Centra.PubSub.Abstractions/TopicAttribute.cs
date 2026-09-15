namespace Centra.PubSub;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class TopicAttribute(string pubSubName, string topic, string? deadLetterTopic = null) : Attribute
{
    public string PubSubName { get; } = pubSubName;
    public string Topic { get; } = topic;
    public string? DeadLetterTopic { get; } = deadLetterTopic;
    public string? RuleFilter { get; init; }
    public int Priority { get; init; } = 0;
    public ConsumerMode ConsumerMode { get; init; } = ConsumerMode.CompetingConsumer;
    public int PrefetchCount { get; init; } = 0;
    public int MaxConcurrentCalls { get; init; } = 0;
    public int MessageTtlSeconds { get; init; } = 0;
    public bool AutoDelete { get; init; } = false;
    public bool EnableTenantOffload { get; init; } = false;
    public string? OffloadTopicPattern { get; init; }
}
