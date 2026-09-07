namespace Centra.Resilience;

/// <summary>
/// Options for configuring a circuit breaker resilience strategy.
/// </summary>
public sealed record CircuitBreakerPolicyOptions(
    double FailureRatio = 0.5,
    TimeSpan SamplingDuration = default,
    int MinimumThroughput = 5,
    TimeSpan BreakDuration = default);
