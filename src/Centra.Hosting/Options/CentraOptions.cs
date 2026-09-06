using Centra.Events;

namespace Centra.Hosting.Options;

public sealed class CentraOptions
{
    public string AppId { get; set; } = "centra-app";
    public string DefaultStateStore { get; set; } = "statestore";
    public string DefaultPubSub { get; set; } = "pubsub";
    public string DefaultLockStore { get; set; } = "lockstore";
    public CloudEventMode DefaultCloudEventMode { get; set; } = CloudEventMode.Binary;
    public string? ControlPlaneEndpoint { get; set; }
    public CentraControlPlaneOptions ControlPlane { get; set; } = new();
}
