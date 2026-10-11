namespace Centra.Locks;

/// <summary>
/// Configuration options for the Centra Distributed Locks building block.
/// </summary>
public sealed class CentraLocksOptions
{
    /// <summary>
    /// Gets or sets the default distributed lock store component name. Defaults to "lockstore".
    /// </summary>
    public string DefaultStore { get; set; } = "lockstore";

    /// <summary>
    /// Gets or sets the default lock lease duration. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the default timeout when acquiring a lock. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
