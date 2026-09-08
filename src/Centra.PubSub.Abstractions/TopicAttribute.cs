namespace Centra.PubSub;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class TopicAttribute(string pubSubName, string topic, string? deadLetterTopic = null) : Attribute
{
    public string PubSubName { get; } = pubSubName;
    public string Topic { get; } = topic;
    public string? DeadLetterTopic { get; } = deadLetterTopic;
    public string? RuleFilter { get; init; }
    public ConsumerMode ConsumerMode { get; init; } = ConsumerMode.CompetingConsumer;
}
