namespace Centra.Providers.Flotilla.Options;

/// <summary>
/// Configuration options for the Centra Flotilla consensus pub/sub provider.
/// </summary>
public sealed class FlotillaProviderOptions
{
    /// <summary>
    /// UDP network addresses of the Flotilla Raft consensus cluster nodes.
    /// </summary>
    public string[] ClusterNodes { get; set; } = ["127.0.0.1:9001", "127.0.0.1:9002", "127.0.0.1:9003"];

    /// <summary>
    /// Default pub/sub component name registered with Centra runtime.
    /// </summary>
    public string DefaultPubSubName { get; set; } = "default";

    /// <summary>
    /// Timeout in milliseconds for client proposal network operations.
    /// </summary>
    public int ClientTimeoutMs { get; set; } = 100;

    /// <summary>
    /// Maximum number of messages per batch during batch publishing.
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Whether to verify IEEE CRC32 checksums on received packet frames.
    /// </summary>
    public bool EnableChecksumVerification { get; set; } = true;

    /// <summary>
    /// Maximum time allowed to drain in-flight messages during graceful shutdown.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
