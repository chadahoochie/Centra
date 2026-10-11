namespace Centra.PubSub;

/// <summary>
/// Configuration options for the Centra Pub/Sub messaging building block.
/// </summary>
public sealed class CentraPubSubOptions
{
    /// <summary>
    /// Gets or sets the default Pub/Sub broker component name. Defaults to "pubsub".
    /// </summary>
    public string DefaultStore { get; set; } = "pubsub";
}
