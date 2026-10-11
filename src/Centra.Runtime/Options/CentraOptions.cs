using Centra.Actors;
using Centra.Events;
using Centra.Locks;
using Centra.PubSub;
using Centra.Resilience;
using Centra.State;
using Centra.Workflows;

namespace Centra;

/// <summary>
/// Root configuration options for the Centra Distributed Application Framework.
/// </summary>
public sealed class CentraOptions
{
    /// <summary>
    /// Gets or sets the application identifier for this service instance in the cluster.
    /// Defaults to "centra-app".
    /// </summary>
    public string AppId { get; set; } = "centra-app";

    /// <summary>
    /// Gets or sets the default CloudEvents serialization mode. Defaults to Binary mode.
    /// </summary>
    public CloudEventMode DefaultCloudEventMode { get; set; } = CloudEventMode.Binary;

    /// <summary>
    /// Gets or sets the default state store name. Forwards to <see cref="CentraStateOptions.DefaultStore"/>.
    /// </summary>
    public string DefaultStateStore
    {
        get => State.DefaultStore;
        set => State.DefaultStore = value;
    }

    /// <summary>
    /// Gets or sets the default pub/sub broker name. Forwards to <see cref="CentraPubSubOptions.DefaultStore"/>.
    /// </summary>
    public string DefaultPubSub
    {
        get => PubSub.DefaultStore;
        set => PubSub.DefaultStore = value;
    }

    /// <summary>
    /// Gets or sets the default distributed lock store name. Forwards to <see cref="CentraLocksOptions.DefaultStore"/>.
    /// </summary>
    public string DefaultLockStore
    {
        get => Locks.DefaultStore;
        set => Locks.DefaultStore = value;
    }

    /// <summary>
    /// Gets or sets the primary Control Plane endpoint URL. Forwards to <see cref="CentraControlPlaneOptions.Endpoint"/>.
    /// </summary>
    public string? ControlPlaneEndpoint
    {
        get => ControlPlane.Endpoint;
        set => ControlPlane.Endpoint = value;
    }

    /// <summary>
    /// Gets or sets the cluster identifier. Forwards to <see cref="CentraControlPlaneOptions.ClusterId"/>.
    /// </summary>
    public string ClusterId
    {
        get => ControlPlane.ClusterId;
        set => ControlPlane.ClusterId = value;
    }

    /// <summary>
    /// Gets or sets configuration options for Control Plane synchronization and coordination.
    /// </summary>
    public CentraControlPlaneOptions ControlPlane { get; set; } = new();

    /// <summary>
    /// Gets or sets configuration options for the State Store building block.
    /// </summary>
    public CentraStateOptions State { get; set; } = new();

    /// <summary>
    /// Gets or sets configuration options for the Pub/Sub messaging building block.
    /// </summary>
    public CentraPubSubOptions PubSub { get; set; } = new();

    /// <summary>
    /// Gets or sets configuration options for the Distributed Locks building block.
    /// </summary>
    public CentraLocksOptions Locks { get; set; } = new();

    /// <summary>
    /// Gets or sets configuration options for the Virtual Actors runtime.
    /// </summary>
    public ActorOptions Actors { get; set; } = new();

    /// <summary>
    /// Gets or sets configuration options for Distributed Workflows and Sagas.
    /// </summary>
    public WorkflowOptions Workflows { get; set; } = new();

    /// <summary>
    /// Gets or sets configuration options for Resilience policies and fault tolerance pipelines.
    /// </summary>
    public CentraResilienceOptions Resilience { get; set; } = new();
}
