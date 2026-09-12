namespace Centra.Sample.Bindings.Jobs;

/// <summary>
/// Domain model representing an inventory snapshot captured by a cron job tick.
/// </summary>
public sealed record InventorySnapshot(
    DateTimeOffset Timestamp,
    int TotalItems,
    long Iteration,
    string ExecutedByNodeId);
