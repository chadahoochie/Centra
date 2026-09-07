namespace Centra.Resilience;

/// <summary>
/// Defines the delay progression strategy for retries.
/// </summary>
public enum CentraBackoffType
{
    Constant,
    Linear,
    Exponential
}
