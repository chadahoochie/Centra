namespace Centra.Resilience;

/// <summary>
/// Options for configuring a timeout resilience strategy.
/// </summary>
public sealed record TimeoutPolicyOptions(TimeSpan Timeout);
