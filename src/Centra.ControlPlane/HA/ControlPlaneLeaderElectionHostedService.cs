using Centra.Locks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.ControlPlane.HA;

public sealed class ControlPlaneLeaderElectionHostedService : BackgroundService
{
    private readonly ControlPlaneLeadershipOptions _options;
    private readonly IControlPlaneLeaderTracker _leaderTracker;
    private readonly IDistributedLockProvider? _lockProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ControlPlaneLeaderElectionHostedService> _logger;

    public ControlPlaneLeaderElectionHostedService(
        ControlPlaneLeadershipOptions options,
        IControlPlaneLeaderTracker leaderTracker,
        ILogger<ControlPlaneLeaderElectionHostedService> logger,
        IDistributedLockProvider? lockProvider = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _leaderTracker = leaderTracker ?? throw new ArgumentNullException(nameof(leaderTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lockProvider = lockProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _leaderTracker.SetLeader(true, _options.PublicEndpoint);
            return;
        }

        if (_lockProvider is null)
        {
            _logger.LogWarning("No IDistributedLockProvider registered for leadership election. Defaulting to standalone active leader at {Endpoint}", _options.PublicEndpoint);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var acquiredLock = await _lockProvider.TryAcquireLockAsync(
                    _options.LockStoreName,
                    _options.LockResource,
                    _options.LeaseDuration,
                    stoppingToken).ConfigureAwait(false);

                if (acquiredLock is not null)
                {
                    await using (acquiredLock.ConfigureAwait(false))
                    {
                        _leaderTracker.SetLeader(true, _options.PublicEndpoint);
                        _logger.LogInformation("Control Plane replica promoted to Active Leader at {Endpoint}", _options.PublicEndpoint);

                        while (!stoppingToken.IsCancellationRequested)
                        {
                            await Task.Delay(_options.RenewInterval, _timeProvider, stoppingToken).ConfigureAwait(false);

                            var renewed = await acquiredLock.RenewAsync(_options.LeaseDuration, stoppingToken).ConfigureAwait(false);
                            if (!renewed)
                            {
                                _logger.LogWarning("Lost leader lock lease for resource {Resource}", _options.LockResource);
                                _leaderTracker.SetLeader(false, null);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    _leaderTracker.SetLeader(false, _leaderTracker.LeaderEndpoint);
                    await Task.Delay(_options.RenewInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during control plane leader election turn");
                _leaderTracker.SetLeader(false, null);
                await Task.Delay(_options.RenewInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }

        _leaderTracker.SetLeader(false, null);
    }
}
