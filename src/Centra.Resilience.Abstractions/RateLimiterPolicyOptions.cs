namespace Centra.Resilience;

/// <summary>
/// Options for configuring a rate limiter resilience strategy.
/// </summary>
public sealed record RateLimiterPolicyOptions(
    int PermitLimit = 100,
    int QueueLimit = 10,
    TimeSpan Window = default);
