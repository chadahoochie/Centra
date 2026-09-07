namespace Centra.Sync;

/// <summary>
/// Data transfer object representing a resilience policy configuration.
/// </summary>
public sealed record ResiliencePolicyDto
{
    public required string PolicyName { get; init; }

    // Retry settings
    public int? MaxRetries { get; init; }
    public string? BackoffType { get; init; }
    public double? BaseDelayMs { get; init; }
    public double? MaxDelayMs { get; init; }
    public bool? UseJitter { get; init; }

    // Circuit Breaker settings
    public double? FailureRatio { get; init; }
    public double? SamplingDurationSeconds { get; init; }
    public int? MinimumThroughput { get; init; }
    public double? BreakDurationSeconds { get; init; }

    // Timeout settings
    public double? TimeoutSeconds { get; init; }

    // Rate Limiter settings
    public int? PermitLimit { get; init; }
    public int? QueueLimit { get; init; }
    public double? WindowSeconds { get; init; }

    // Bulkhead settings
    public int? MaxParallelism { get; init; }
    public int? MaxQueuedActions { get; init; }
}
