namespace Centra.Resilience;

/// <summary>
/// Defines the type of resilience policy strategy.
/// </summary>
public enum CentraPolicyType
{
    Retry,
    CircuitBreaker,
    Timeout,
    RateLimiter,
    Bulkhead,
    Fallback
}
