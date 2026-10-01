namespace Centra.Providers.Flotilla.Grpc.Options;

/// <summary>
/// Configuration options for the Centra Flotilla gRPC consensus pub/sub provider.
/// </summary>
public sealed class FlotillaGrpcOptions
{
    /// <summary>
    /// Network URIs of the Flotilla Raft consensus cluster nodes.
    /// </summary>
    public string[] ClusterNodes { get; set; } = ["http://127.0.0.1:9001", "http://127.0.0.1:9002", "http://127.0.0.1:9003"];

    /// <summary>
    /// Default pub/sub component name registered with Centra runtime.
    /// </summary>
    public string DefaultPubSubName { get; set; } = "default";

    /// <summary>
    /// Timeout in milliseconds for client proposal network operations.
    /// </summary>
    public int ClientTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// Maximum number of messages per batch during batch publishing.
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum time allowed to drain in-flight messages during graceful shutdown.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
