using System.Diagnostics.Metrics;

namespace Centra.Diagnostics;

public static class CentraMeters
{
    public const string MeterName = "Centra";
    public const string Version = "1.0.0";

    public static readonly Meter Meter = new(MeterName, Version);

    // State instruments
    private static readonly Counter<long> StateOperationsCounter =
        Meter.CreateCounter<long>("centra.state.operations.total", "ea", "Total count of state operations");
    private static readonly Histogram<double> StateOperationDuration =
        Meter.CreateHistogram<double>("centra.state.operation.duration", "ms", "Duration of state operations");

    // Pub/Sub instruments
    private static readonly Counter<long> PubSubPublishedCounter =
        Meter.CreateCounter<long>("centra.pubsub.messages.published", "ea", "Total messages published to topics");
    private static readonly Histogram<double> PubSubPublishDuration =
        Meter.CreateHistogram<double>("centra.pubsub.publish.duration", "ms", "Duration to publish a message");
    private static readonly Counter<long> PubSubConsumedCounter =
        Meter.CreateCounter<long>("centra.pubsub.messages.consumed", "ea", "Total messages consumed from topics");
    private static readonly Histogram<double> PubSubProcessDuration =
        Meter.CreateHistogram<double>("centra.pubsub.process.duration", "ms", "Duration to process a message");

    // Invocation instruments
    private static readonly Counter<long> InvocationRequestsCounter =
        Meter.CreateCounter<long>("centra.invocation.requests.total", "ea", "Total service invocation requests");
    private static readonly Histogram<double> InvocationDuration =
        Meter.CreateHistogram<double>("centra.invocation.duration", "ms", "Duration of service invocation calls");

    // Lock instruments
    private static readonly Counter<long> LockAcquisitionsCounter =
        Meter.CreateCounter<long>("centra.lock.acquisitions.total", "ea", "Total distributed lock acquisitions");
    private static readonly Histogram<double> LockHoldDuration =
        Meter.CreateHistogram<double>("centra.lock.hold.duration", "ms", "Duration a distributed lock was held");

    public static void RecordStateOperation(string store, string operation, string status, double durationMs)
    {
        StateOperationsCounter.Add(1,
            new KeyValuePair<string, object?>("centra.store.name", store),
            new KeyValuePair<string, object?>("centra.operation", operation),
            new KeyValuePair<string, object?>("status", status));

        StateOperationDuration.Record(durationMs,
            new KeyValuePair<string, object?>("centra.store.name", store),
            new KeyValuePair<string, object?>("centra.operation", operation),
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordPubSubPublished(string pubsub, string topic, string status, double durationMs)
    {
        PubSubPublishedCounter.Add(1,
            new KeyValuePair<string, object?>("centra.pubsub.name", pubsub),
            new KeyValuePair<string, object?>("centra.topic", topic),
            new KeyValuePair<string, object?>("status", status));

        PubSubPublishDuration.Record(durationMs,
            new KeyValuePair<string, object?>("centra.pubsub.name", pubsub),
            new KeyValuePair<string, object?>("centra.topic", topic),
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordPubSubConsumed(string pubsub, string topic, string status, double durationMs)
    {
        PubSubConsumedCounter.Add(1,
            new KeyValuePair<string, object?>("centra.pubsub.name", pubsub),
            new KeyValuePair<string, object?>("centra.topic", topic),
            new KeyValuePair<string, object?>("status", status));

        PubSubProcessDuration.Record(durationMs,
            new KeyValuePair<string, object?>("centra.pubsub.name", pubsub),
            new KeyValuePair<string, object?>("centra.topic", topic),
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordInvocation(string appId, string method, string status, double durationMs)
    {
        InvocationRequestsCounter.Add(1,
            new KeyValuePair<string, object?>("centra.target.app_id", appId),
            new KeyValuePair<string, object?>("centra.method", method),
            new KeyValuePair<string, object?>("status", status));

        InvocationDuration.Record(durationMs,
            new KeyValuePair<string, object?>("centra.target.app_id", appId),
            new KeyValuePair<string, object?>("centra.method", method),
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordLockAcquisition(string lockStore, string status, double durationMs)
    {
        LockAcquisitionsCounter.Add(1,
            new KeyValuePair<string, object?>("centra.lock_store.name", lockStore),
            new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordLockHold(string lockStore, double holdDurationMs)
    {
        LockHoldDuration.Record(holdDurationMs,
            new KeyValuePair<string, object?>("centra.lock_store.name", lockStore));
    }
}
