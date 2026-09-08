using System.Collections.Concurrent;
using Centra.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centra.Invocation;

public sealed class ControlPlaneServiceEndpointResolver : IServiceEndpointResolver
{
    private readonly IControlPlaneClient _controlPlaneClient;
    private readonly ILogger<ControlPlaneServiceEndpointResolver> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, int> _roundRobinIndex = new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile IReadOnlyCollection<ServiceNodeDto>? _cachedTopology;
    private long _expiresAtTimestamp;

    public ControlPlaneServiceEndpointResolver(
        IControlPlaneClient controlPlaneClient,
        ILogger<ControlPlaneServiceEndpointResolver>? logger = null,
        TimeProvider? timeProvider = null,
        TimeSpan? cacheTtl = null)
    {
        _controlPlaneClient = controlPlaneClient ?? throw new ArgumentNullException(nameof(controlPlaneClient));
        _logger = logger ?? NullLogger<ControlPlaneServiceEndpointResolver>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(5);
    }

    public async ValueTask<Uri?> ResolveEndpointAsync(string serviceAppId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceAppId);

        try
        {
            var nodes = await GetTopologyAsync(cancellationToken).ConfigureAwait(false);
            var matching = nodes
                .Where(n => string.Equals(n.AppId, serviceAppId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(n.Status, "Healthy", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count == 0)
            {
                _logger.LogDebug("No healthy instances found in Control Plane topology for AppId '{AppId}'. Falling back to default URI.", serviceAppId);
                return new Uri($"http://{serviceAppId}/", UriKind.Absolute);
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
                return parsedUri;
            }

            return new Uri($"http://{serviceAppId}/", UriKind.Absolute);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve endpoint for AppId '{AppId}' from Control Plane. Falling back to default URI.", serviceAppId);
            return new Uri($"http://{serviceAppId}/", UriKind.Absolute);
        }
    }

    private async ValueTask<IReadOnlyCollection<ServiceNodeDto>> GetTopologyAsync(CancellationToken cancellationToken)
    {
        var current = _cachedTopology;
        var now = _timeProvider.GetTimestamp();

        if (current is not null && now < _expiresAtTimestamp)
        {
            return current;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = _cachedTopology;
            now = _timeProvider.GetTimestamp();
            if (current is not null && now < _expiresAtTimestamp)
            {
                return current;
            }

            var fresh = await _controlPlaneClient.GetTopologyAsync(cancellationToken).ConfigureAwait(false);
            _cachedTopology = fresh;
            _expiresAtTimestamp = now + (long)(_cacheTtl.TotalSeconds * _timeProvider.TimestampFrequency);
            return fresh;
        }
        catch (Exception) when (_cachedTopology is not null)
        {
            // Return stale topology during control plane transient unavailability
            return _cachedTopology;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
