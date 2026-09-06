using Centra.Components;

namespace Centra.Sync;

public sealed record ComponentSyncEventDto
{
    public required ComponentSyncAction Action { get; init; }
    public ComponentDefinition? Definition { get; init; }
    public string? ComponentName { get; init; }
    public long Revision { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
}
