using System.Collections.Concurrent;
using Centra.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centra.Invocation;

public sealed class ControlPlaneServiceEndpointResolver : IServiceEndpointResolver
{
    private readonly IClusterTopologyProvider _topologyProvider;
    private readonly ILogger<ControlPlaneServiceEndpointResolver> _logger;
    private readonly ConcurrentDictionary<string, int> _roundRobinIndex = new(StringComparer.OrdinalIgnoreCase);

    public ControlPlaneServiceEndpointResolver(
        IClusterTopologyProvider topologyProvider,
        ILogger<ControlPlaneServiceEndpointResolver>? logger = null)
    {
        _topologyProvider = topologyProvider ?? throw new ArgumentNullException(nameof(topologyProvider));
        _logger = logger ?? NullLogger<ControlPlaneServiceEndpointResolver>.Instance;
    }

    public ValueTask<Uri?> ResolveEndpointAsync(string serviceAppId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceAppId);

        try
        {
            var nodes = _topologyProvider.GetSnapshot();
            var matching = nodes
                .Where(n => string.Equals(n.AppId, serviceAppId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(n.Status, "Healthy", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count == 0)
            {
                _logger.LogDebug("No healthy instances found in Control Plane topology for AppId '{AppId}'. Falling back to default URI.", serviceAppId);
                return ValueTask.FromResult<Uri?>(new Uri($"http://{serviceAppId}/", UriKind.Absolute));
            }

            var index = _roundRobinIndex.AddOrUpdate(serviceAppId, 0, (_, current) => (current + 1) % matching.Count);
            var selected = matching[Math.Abs(index) % matching.Count];

            // Check metadata for explicit Address, Endpoint, or Url
            string? targetAddress = null;
            if (selected.Metadata is not null)
            {
                selected.Metadata.TryGetValue("address", out targetAddress);
                if (string.IsNullOrWhiteSpace(targetAddress))
                {
                    selected.Metadata.TryGetValue("endpoint", out targetAddress);
                }
                if (string.IsNullOrWhiteSpace(targetAddress))
                {
                    selected.Metadata.TryGetValue("url", out targetAddress);
                }
            }

            if (!string.IsNullOrWhiteSpace(targetAddress) && Uri.TryCreate(targetAddress.TrimEnd('/') + "/", UriKind.Absolute, out var parsedUri))
            {
                _logger.LogDebug("Resolved AppId '{AppId}' (Instance: {InstanceId}) to {Uri}", serviceAppId, selected.InstanceId, parsedUri);
                return ValueTask.FromResult<Uri?>(parsedUri);
            }

            return ValueTask.FromResult<Uri?>(new Uri($"http://{serviceAppId}/", UriKind.Absolute));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve endpoint for AppId '{AppId}' from Control Plane. Falling back to default URI.", serviceAppId);
            return ValueTask.FromResult<Uri?>(new Uri($"http://{serviceAppId}/", UriKind.Absolute));
        }
    }
}
