using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centra.Sync;

/// <summary>
/// Polls Control Plane topology on a fixed interval and exposes it as an
/// <see cref="IClusterTopologyProvider"/>, diffing by <see cref="ServiceNodeDto.InstanceId"/> so
/// consumers only react to actual membership changes.
/// </summary>
public sealed class ClusterTopologyProviderHostedService : BackgroundService, IClusterTopologyProvider
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly IControlPlaneClient _controlPlaneClient;
    private readonly ILogger<ClusterTopologyProviderHostedService> _logger;
    private volatile IReadOnlyCollection<ServiceNodeDto> _snapshot = Array.Empty<ServiceNodeDto>();

    public event EventHandler<ClusterTopologyChangedEventArgs>? TopologyChanged;

    public ClusterTopologyProviderHostedService(
        IControlPlaneClient controlPlaneClient,
        ILogger<ClusterTopologyProviderHostedService>? logger = null)
    {
        _controlPlaneClient = controlPlaneClient ?? throw new ArgumentNullException(nameof(controlPlaneClient));
        _logger = logger ?? NullLogger<ClusterTopologyProviderHostedService>.Instance;
    }

    public IReadOnlyCollection<ServiceNodeDto> GetSnapshot() => _snapshot;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var fresh = await _controlPlaneClient.GetTopologyAsync(stoppingToken).ConfigureAwait(false);
                var previous = _snapshot;
                _snapshot = fresh;

                var previousIds = previous.Select(n => n.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var freshIds = fresh.Select(n => n.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var added = fresh.Where(n => !previousIds.Contains(n.InstanceId)).ToList();
                var removed = previous.Where(n => !freshIds.Contains(n.InstanceId)).ToList();

                if (added.Count > 0 || removed.Count > 0)
                {
                    TopologyChanged?.Invoke(this, new ClusterTopologyChangedEventArgs
                    {
                        AddedNodes = added,
                        RemovedNodes = removed,
                        CurrentSnapshot = fresh
                    });
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Failed to refresh cluster topology from Control Plane.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
