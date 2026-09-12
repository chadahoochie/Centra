using Centra.Core.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Hosting.HostedServices;

/// <summary>
/// Hosted service managing background workflow processing and durable timer evaluations.
/// </summary>
public sealed class CentraWorkflowHostedService : BackgroundService
{
    private readonly IWorkflowEngine _engine;
    private readonly WorkflowOptions _options;
    private readonly DurableWorkflowTimerCoordinator? _timerCoordinator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CentraWorkflowHostedService>? _logger;

    public CentraWorkflowHostedService(
        IWorkflowEngine engine,
        WorkflowOptions options,
        TimeProvider? timeProvider = null,
        ILogger<CentraWorkflowHostedService>? logger = null)
        : this(engine, options, null, timeProvider, logger)
    {
    }

    public CentraWorkflowHostedService(
        IWorkflowEngine engine,
        WorkflowOptions options,
        DurableWorkflowTimerCoordinator? timerCoordinator,
        TimeProvider? timeProvider = null,
        ILogger<CentraWorkflowHostedService>? logger = null)
    {
        _engine = engine;
        _options = options;
        _timerCoordinator = timerCoordinator;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger?.LogInformation("Centra Workflow background runner started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_timerCoordinator is not null)
                {
                    await _timerCoordinator.ProcessDueTimersAsync(stoppingToken).ConfigureAwait(false);
                }

                await Task.Delay(_options.PollingInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error occurred in Centra workflow background loop.");
            }
        }

        _logger?.LogInformation("Centra Workflow background runner stopped.");
    }
}
