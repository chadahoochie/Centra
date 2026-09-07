namespace Centra.Core.Workflows;

/// <summary>
/// Configuration options for Centra distributed workflows.
/// </summary>
public sealed class WorkflowOptions
{
    public string DefaultStateStore { get; set; } = "statestore";
    public string DefaultPubSub { get; set; } = "pubsub";
    public string DefaultLockStore { get; set; } = "lockstore";
    public TimeSpan DefaultExecutionTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
}
