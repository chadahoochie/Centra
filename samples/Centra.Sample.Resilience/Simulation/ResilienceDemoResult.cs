namespace Centra.Sample.Resilience.Simulation;

public sealed record ResilienceDemoResult(
    bool BaselineSuccess,
    bool TransientRetrySuccess,
    int TransientRetryAttempts,
    bool CircuitBreakerTripped,
    bool CircuitBreakerRecovered,
    bool TimeoutTriggered,
    bool DynamicPolicyUpdateApplied,
    TimeSpan ElapsedDuration);
