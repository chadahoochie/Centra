using Microsoft.Extensions.Logging;

namespace Centra.ControlPlane.Diagnostics;

public static partial class ControlPlaneLogMessages
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Component {ComponentName} of type {ComponentType} registered with revision {Revision}")]
    public static partial void ComponentRegistered(ILogger logger, string componentName, string componentType, long revision);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Component {ComponentName} removed from catalog")]
    public static partial void ComponentRemoved(ILogger logger, string componentName);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Heartbeat received from app {AppId} (Instance: {InstanceId}) with status {Status}")]
    public static partial void HeartbeatReceived(ILogger logger, string appId, string instanceId, string status);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Sync event {EventType} dispatched for component {ComponentName}")]
    public static partial void SyncEventDispatched(ILogger logger, string eventType, string? componentName);
}
