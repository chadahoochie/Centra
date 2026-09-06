using Microsoft.Extensions.Logging;

namespace Centra.Diagnostics;

public static partial class CentraLogMessages
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Centra initialized application '{AppId}'")]
    public static partial void LogCentraInitialized(this ILogger logger, string appId);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Component '{ComponentName}' of type '{ComponentType}' registered using provider '{Provider}'")]
    public static partial void LogComponentRegistered(this ILogger logger, string componentName, string componentType, string provider);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Debug,
        Message = "Published event '{EventType}' to pubsub '{PubSubName}' topic '{Topic}' with ID '{EventId}'")]
    public static partial void LogEventPublished(this ILogger logger, string eventType, string pubSubName, string topic, string eventId);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Debug,
        Message = "Processing event '{EventType}' from pubsub '{PubSubName}' topic '{Topic}' with ID '{EventId}'")]
    public static partial void LogEventProcessing(this ILogger logger, string eventType, string pubSubName, string topic, string eventId);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Failed to process event '{EventType}' from pubsub '{PubSubName}' topic '{Topic}' with ID '{EventId}'")]
    public static partial void LogEventProcessingFailed(this ILogger logger, Exception exception, string eventType, string pubSubName, string topic, string eventId);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Debug,
        Message = "State store '{StoreName}' executed '{Operation}' for key '{Key}'")]
    public static partial void LogStateOperation(this ILogger logger, string storeName, string operation, string key);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Debug,
        Message = "Invoking service '{AppId}' method '{Method}' with verb '{Verb}'")]
    public static partial void LogServiceInvoking(this ILogger logger, string appId, string method, string verb);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Debug,
        Message = "Acquired distributed lock '{LockId}' for resource '{ResourceId}' in store '{StoreName}'")]
    public static partial void LogLockAcquired(this ILogger logger, string lockId, string resourceId, string storeName);
}
