namespace Centra.Sample.Bindings.Jobs;

public sealed record InventorySnapshot(DateTimeOffset Timestamp, int TotalItems, long Iteration);
