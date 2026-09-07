namespace Centra.Resilience;

/// <summary>
/// Options for configuring a bulkhead (concurrency limiter) resilience strategy.
/// </summary>
public sealed record BulkheadPolicyOptions(
    int MaxParallelism = 50,
    int MaxQueuedActions = 20);
