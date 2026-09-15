using Centra.PubSub.Tenancy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.HostedServices;

public sealed class TenantOffloadReaperHostedService : BackgroundService
{
    private readonly ITenantOffloadCoordinator _coordinator;
    private readonly IOptions<TenantOffloadOptions> _options;
    private readonly ILogger<TenantOffloadReaperHostedService> _logger;

    public TenantOffloadReaperHostedService(
        ITenantOffloadCoordinator coordinator,
        IOptions<TenantOffloadOptions> options,
        ILogger<TenantOffloadReaperHostedService> logger)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                await _coordinator.CleanupIdleResourcesAsync(stoppingToken).ConfigureAwait(false);
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
