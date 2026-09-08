using Centra.Invocation;
using Centra.Sync;

namespace Centra.Sample.DockerStack.Cluster;

/// <summary>
/// Resolves an actor ring node id (which is this cluster's per-replica InstanceId, not the
/// shared AppId used for round-robin service invocation) directly to the peer's reachable
/// address, read from the "address" heartbeat metadata each replica reports on startup.
/// Used only by the actor proxy factory's dedicated IServiceInvoker - normal peer-to-peer
/// service invocation still resolves by AppId via ControlPlaneServiceEndpointResolver.
/// </summary>
public sealed class PeerInstanceEndpointResolver : IServiceEndpointResolver
{
    private readonly IControlPlaneClient _controlPlaneClient;

    public PeerInstanceEndpointResolver(IControlPlaneClient controlPlaneClient)
    {
        _controlPlaneClient = controlPlaneClient ?? throw new ArgumentNullException(nameof(controlPlaneClient));
    }

    public async ValueTask<Uri?> ResolveEndpointAsync(string serviceAppId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceAppId);

        var nodes = await _controlPlaneClient.GetTopologyAsync(cancellationToken).ConfigureAwait(false);

        var node = nodes.FirstOrDefault(n =>
            string.Equals(n.InstanceId, serviceAppId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(n.Status, "Healthy", StringComparison.OrdinalIgnoreCase));

        if (node?.Metadata is not null && node.Metadata.TryGetValue("address", out var address) &&
            Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var parsedUri))
        {
            return parsedUri;
        }

        return new Uri($"http://{serviceAppId}/", UriKind.Absolute);
    }
}
