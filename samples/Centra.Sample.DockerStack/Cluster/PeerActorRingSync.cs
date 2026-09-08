using Centra.Core.Actors;
using Centra.Hosting.Options;
using Centra.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centra.Sample.DockerStack.Cluster;

/// <summary>
/// Keeps the actor placement ConsistentHashRing in sync with the cluster's real membership, by
/// polling Control Plane topology for healthy peers sharing this node's AppId and adding/removing
/// their InstanceId from the ring. Without this, the ring (seeded with only the local InstanceId
/// at startup) would never learn about the other replicas and every actor would resolve as local.
/// </summary>
public sealed class PeerActorRingSync : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly ConsistentHashRing _ring;
    private readonly IControlPlaneClient _controlPlaneClient;
    private readonly CentraOptions _options;
    private readonly ILogger<PeerActorRingSync> _logger;
    private readonly HashSet<string> _knownPeers = new(StringComparer.OrdinalIgnoreCase);

    public PeerActorRingSync(
        ConsistentHashRing ring,
        IControlPlaneClient controlPlaneClient,
        IOptions<CentraOptions> options,
        ILogger<PeerActorRingSync> logger)
    {
        _ring = ring ?? throw new ArgumentNullException(nameof(ring));
        _controlPlaneClient = controlPlaneClient ?? throw new ArgumentNullException(nameof(controlPlaneClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nodes = await _controlPlaneClient.GetTopologyAsync(stoppingToken).ConfigureAwait(false);

                var healthyPeers = nodes
                    .Where(n =>
                        string.Equals(n.AppId, _options.AppId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(n.Status, "Healthy", StringComparison.OrdinalIgnoreCase))
                    .Select(n => n.InstanceId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var peer in healthyPeers)
                {
                    if (_knownPeers.Add(peer))
                    {
                        _ring.AddNode(peer);
                        _logger.LogInformation("Actor ring: added peer node {InstanceId}", peer);
                    }
                }

                foreach (var peer in _knownPeers.Where(p => !healthyPeers.Contains(p)).ToArray())
                {
                    _knownPeers.Remove(peer);
                    _ring.RemoveNode(peer);
                    _logger.LogInformation("Actor ring: removed stale peer node {InstanceId}", peer);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Failed to sync actor ring from Control Plane topology.");
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
