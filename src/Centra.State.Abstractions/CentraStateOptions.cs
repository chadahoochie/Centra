namespace Centra.State;

/// <summary>
/// Configuration options for the Centra State Store building block.
/// </summary>
public sealed class CentraStateOptions
{
    /// <summary>
    /// Gets or sets the default state store component name. Defaults to "statestore".
    /// </summary>
    public string DefaultStore { get; set; } = "statestore";
}
