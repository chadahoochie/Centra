using System.Diagnostics.Metrics;

namespace Centra.ControlPlane.Diagnostics;

public static class ControlPlaneMeters
{
    public const string MeterName = "Centra.ControlPlane";
    public const string Version = "1.0.0";

    public static readonly Meter Meter = new(MeterName, Version);

    private static readonly Counter<long> ComponentsRegisteredCounter =
        Meter.CreateCounter<long>("centra.controlplane.components.registered", "count", "Total number of components registered");

    private static readonly Counter<long> SyncEventsDispatchedCounter =
        Meter.CreateCounter<long>("centra.controlplane.sync.events.dispatched", "count", "Total number of sync events dispatched");

    private static readonly Counter<long> HeartbeatsReceivedCounter =
        Meter.CreateCounter<long>("centra.controlplane.heartbeats.received", "count", "Total number of client heartbeats received");

    private static readonly Histogram<double> HeartbeatLatencyHistogram =
        Meter.CreateHistogram<double>("centra.controlplane.heartbeats.latency", "ms", "Latency of client heartbeat processing");

    private static readonly Counter<long> AdmissionRejectedCounter =
        Meter.CreateCounter<long>("centra.controlplane.security.admission.rejected", "count", "Total number of rejected cluster admission requests");

    private static readonly UpDownCounter<long> ActiveNodesCounter =
        Meter.CreateUpDownCounter<long>("centra.controlplane.nodes.active", "count", "Total active nodes across clusters");

    public static void RecordComponentRegistered(string componentName, string componentType)
    {
        ComponentsRegisteredCounter.Add(1, new KeyValuePair<string, object?>("component.name", componentName), new KeyValuePair<string, object?>("component.type", componentType));
    }

    public static void RecordSyncEventDispatched(string eventType)
    {
        SyncEventsDispatchedCounter.Add(1, new KeyValuePair<string, object?>("event.type", eventType));
    }

    public static void RecordHeartbeatReceived(string appId, string status)
    {
        HeartbeatsReceivedCounter.Add(1, new KeyValuePair<string, object?>("app.id", appId), new KeyValuePair<string, object?>("status", status));
    }

    public static void RecordHeartbeatLatency(double latencyMs, string clusterId)
    {
        HeartbeatLatencyHistogram.Record(latencyMs, new KeyValuePair<string, object?>("cluster.id", clusterId));
    }

    public static void RecordAdmissionRejected(string clusterId, string reason)
    {
        AdmissionRejectedCounter.Add(1, new KeyValuePair<string, object?>("cluster.id", clusterId), new KeyValuePair<string, object?>("reason", reason));
    }

    public static void RecordActiveNodeDelta(int delta, string clusterId)
    {
        ActiveNodesCounter.Add(delta, new KeyValuePair<string, object?>("cluster.id", clusterId));
    }
}
