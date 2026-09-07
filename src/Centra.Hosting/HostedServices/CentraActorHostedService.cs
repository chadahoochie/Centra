using Centra.Actors;
using Centra.Core.Actors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Hosting.HostedServices;

public sealed class CentraActorHostedService : BackgroundService
{
    private readonly ActorManager _actorManager;
    private readonly ActorReminderCoordinator _reminderCoordinator;
    private readonly ActorOptions _options;
    private readonly ILogger<CentraActorHostedService>? _logger;

    public CentraActorHostedService(
        ActorManager actorManager,
        ActorReminderCoordinator reminderCoordinator,
        ActorOptions options,
        ILogger<CentraActorHostedService>? logger = null)
    {
        _actorManager = actorManager;
        _reminderCoordinator = reminderCoordinator;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _reminderCoordinator.TickAsync(stoppingToken).ConfigureAwait(false);
                await _actorManager.PassivateIdleActorsAsync(_options.ActorIdleTimeout, stoppingToken).ConfigureAwait(false);

                await Task.Delay(_options.ReminderInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error occurred during actor reminder / passivation tick.");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await _actorManager.DisposeAsync().ConfigureAwait(false);
    }
}
