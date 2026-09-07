namespace Centra.Resilience;

/// <summary>
/// Represents the operating state of a circuit breaker.
/// </summary>
public enum CentraCircuitState
{
    Closed,
    Open,
    HalfOpen,
    Isolated
}
