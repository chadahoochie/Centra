namespace Centra.Sync;

/// <summary>
/// Data transfer object for streaming resilience policy change events over SSE.
/// </summary>
public sealed record ResilienceSyncEventDto
{
    public required string Action { get; init; } // "Upserted" or "Deleted"
    public required string PolicyName { get; init; }
    public ResiliencePolicyDto? Policy { get; init; }
    public long Revision { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
}
