using Centra.Locks;
using Centra.PubSub.Tenancy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centra.PubSub.HostedServices;

public sealed class TenantOffloadReaperHostedService : BackgroundService
{
    private readonly ITenantOffloadCoordinator _coordinator;
    private readonly IOptions<TenantOffloadOptions> _options;
    private readonly ILogger<TenantOffloadReaperHostedService> _logger;
    private readonly ITenantOffloadReaperLockCoordinator _lockCoordinator;

    public TenantOffloadReaperHostedService(
        ITenantOffloadCoordinator coordinator,
        IOptions<TenantOffloadOptions> options,
        ILogger<TenantOffloadReaperHostedService> logger)
        : this(coordinator, options, logger, null, null)
    {
    }

    public TenantOffloadReaperHostedService(
        ITenantOffloadCoordinator coordinator,
        IOptions<TenantOffloadOptions> options,
        ILogger<TenantOffloadReaperHostedService> logger,
        IDistributedLockProvider? lockProvider,
        ITenantOffloadReaperLockCoordinator? lockCoordinator = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lockCoordinator = lockCoordinator ?? new TenantOffloadReaperLockCoordinator(lockProvider, options.Value);

        if (options.Value.OffloadStrategy == TenantOffloadStrategyType.EphemeralBrokerTopic && lockProvider is null && lockCoordinator is null)
        {
            _logger.LogWarning("CRITICAL WARNING: Tenant offload strategy is configured as EphemeralBrokerTopic, but no IDistributedLockProvider or Control Plane is registered. In multi-instance environments, uncoordinated local reapers will cause split-brain queue teardown and potential message loss. Ensure an IDistributedLockProvider is registered or switch to BoundedShardBrokerTopic.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _options.Value.LaneIdleTimeout > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(Math.Max(10, _options.Value.LaneIdleTimeout.TotalMilliseconds / 2))
            : TimeSpan.FromSeconds(15);

        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);

                var (acquired, @lock) = await _lockCoordinator.TryAcquireReaperLockAsync(stoppingToken).ConfigureAwait(false);
                if (!acquired)
                {
                    _logger.LogDebug("Reaper lock held by peer cluster replica; skipping tick");
                    continue;
                }

                try
                {
                    await _coordinator.CleanupIdleResourcesAsync(stoppingToken).ConfigureAwait(false);
                }
                finally
                {
                    if (@lock is not null)
                    {
                        await @lock.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while executing tenant offload cleanup reaper");
            }
        }
    }
}
