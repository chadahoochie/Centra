namespace Centra.Workflows;

/// <summary>
/// Execution options for an activity invocation.
/// </summary>
public sealed class ActivityOptions
{
    public TimeSpan? Timeout { get; set; }
    public string? ResiliencePolicyName { get; set; }
    public int? MaxRetries { get; set; }
}
