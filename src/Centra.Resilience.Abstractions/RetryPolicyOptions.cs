namespace Centra.Resilience;

/// <summary>
/// Options for configuring a retry resilience strategy.
/// </summary>
public sealed record RetryPolicyOptions(
    int MaxRetries = 3,
    CentraBackoffType BackoffType = CentraBackoffType.Exponential,
    TimeSpan BaseDelay = default,
    TimeSpan MaxDelay = default,
    bool UseJitter = true);
