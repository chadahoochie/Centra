namespace Centra.Hosting.Routing;

public sealed record CentraTopicRegistration(
    string PubSubName,
    string Topic,
    Type EventType,
    Type HandlerType,
    string? DeadLetterTopic = null);
