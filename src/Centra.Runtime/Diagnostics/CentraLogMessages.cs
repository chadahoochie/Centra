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

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Warning,
        Message = "Resilience pipeline '{PipelineName}' retry attempt #{AttemptNumber} after delay {DelayMs}ms due to: {Reason}")]
    public static partial void LogResilienceRetry(this ILogger logger, string pipelineName, int attemptNumber, double delayMs, string reason);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Error,
        Message = "Resilience pipeline '{PipelineName}' circuit breaker opened for {BreakDurationMs}ms")]
    public static partial void LogResilienceCircuitOpened(this ILogger logger, string pipelineName, double breakDurationMs);

    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Information,
        Message = "Resilience pipeline '{PipelineName}' circuit breaker reset to closed")]
    public static partial void LogResilienceCircuitClosed(this ILogger logger, string pipelineName);

    [LoggerMessage(
        EventId = 6004,
        Level = LogLevel.Warning,
        Message = "Resilience pipeline '{PipelineName}' circuit breaker testing in half-open state")]
    public static partial void LogResilienceCircuitHalfOpened(this ILogger logger, string pipelineName);

    [LoggerMessage(
        EventId = 6005,
        Level = LogLevel.Error,
        Message = "Resilience pipeline '{PipelineName}' timed out after {TimeoutMs}ms")]
    public static partial void LogResilienceTimeout(this ILogger logger, string pipelineName, double timeoutMs);
}
