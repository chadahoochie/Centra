using Centra.Components;

namespace Centra.ControlPlane.Sync;

public sealed record ComponentSyncEvent(
    ComponentSyncEventType Type,
    ComponentDefinition? Definition,
    string? ComponentName,
    long Revision,
    DateTimeOffset TimestampUtc);
