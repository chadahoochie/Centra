namespace Centra.Workflows;

/// <summary>
/// Configuration options for Centra distributed workflows.
/// </summary>
public class WorkflowOptions
{
    /// <summary>
    /// Gets or sets the default workflow execution timeout. Defaults to 30 minutes.
    /// </summary>
    public TimeSpan DefaultExecutionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets or sets the polling interval for durable workflow timers and suspended workflows. Defaults to 1 second.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the default state store name for workflow state and history. Defaults to "statestore".
    /// </summary>
    public string DefaultStateStore { get; set; } = "statestore";

    /// <summary>
    /// Gets or sets the default pub/sub broker name for external workflow events. Defaults to "pubsub".
    /// </summary>
    public string DefaultPubSub { get; set; } = "pubsub";

    /// <summary>
    /// Gets or sets the default distributed lock store name for workflow orchestration locks. Defaults to "lockstore".
    /// </summary>
    public string DefaultLockStore { get; set; } = "lockstore";

    /// <summary>
    /// Gets whether <see cref="DefaultStateStore"/> was explicitly set.
    /// </summary>
    public bool HasExplicitStateStore => DefaultStateStore != "statestore";

    /// <summary>
    /// Gets whether <see cref="DefaultPubSub"/> was explicitly set.
    /// </summary>
    public bool HasExplicitPubSub => DefaultPubSub != "pubsub";

    /// <summary>
    /// Gets whether <see cref="DefaultLockStore"/> was explicitly set.
    /// </summary>
    public bool HasExplicitLockStore => DefaultLockStore != "lockstore";
}
