using Centra.Core.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centra.Hosting.HostedServices;

/// <summary>
/// Background service that periodically sweeps due durable workflow timers and triggers turn execution.
/// </summary>
public sealed class CentraWorkflowTimerHostedService : BackgroundService
{
    private readonly DurableWorkflowTimerCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _pollingInterval;
    private readonly ILogger<CentraWorkflowTimerHostedService> _logger;

    public CentraWorkflowTimerHostedService(
        DurableWorkflowTimerCoordinator coordinator,
        TimeProvider? timeProvider = null,
        TimeSpan? pollingInterval = null,
        ILogger<CentraWorkflowTimerHostedService>? logger = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(1);
        _logger = logger ?? NullLogger<CentraWorkflowTimerHostedService>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Centra Workflow Durable Timer coordinator started with interval {Interval}", _pollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _coordinator.ProcessDueTimersAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in durable workflow timer sweep loop");
            }

            try
            {
                await Task.Delay(_pollingInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Centra Workflow Durable Timer coordinator stopped.");
    }
}
