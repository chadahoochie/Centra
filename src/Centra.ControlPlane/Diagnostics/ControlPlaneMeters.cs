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
}
