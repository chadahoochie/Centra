namespace Centra.Resilience;

/// <summary>
/// Master definition combining resilience policy options for a named pipeline.
/// </summary>
public sealed record CentraResiliencePolicyDefinition(
    string PolicyName,
    RetryPolicyOptions? Retry = null,
    CircuitBreakerPolicyOptions? CircuitBreaker = null,
    TimeoutPolicyOptions? Timeout = null,
    RateLimiterPolicyOptions? RateLimiter = null,
    BulkheadPolicyOptions? Bulkhead = null);
