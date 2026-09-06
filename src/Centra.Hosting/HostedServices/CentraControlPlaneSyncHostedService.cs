using Centra.Components;
using Centra.Hosting.Options;
using Centra.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.HostedServices;

public sealed class CentraControlPlaneSyncHostedService : BackgroundService
{
    private readonly IControlPlaneClient _client;
    private readonly IComponentRegistry _registry;
    private readonly CentraOptions _options;
    private readonly ILogger<CentraControlPlaneSyncHostedService> _logger;
    private readonly TimeProvider _timeProvider;

    public CentraControlPlaneSyncHostedService(
        IControlPlaneClient client,
        IComponentRegistry registry,
        IOptions<CentraOptions> options,
        ILogger<CentraControlPlaneSyncHostedService> logger,
        TimeProvider? timeProvider = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = _options.ControlPlaneEndpoint ?? _options.ControlPlane.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("No ControlPlane endpoint configured. Live sync disabled.");
            return;
        }

        var instanceId = _options.ControlPlane.InstanceId;

        // 1. Initial full fetch
        try
        {
            _logger.LogInformation("Performing initial component sync with Control Plane at {Endpoint}", endpoint);
            var initialComponents = await _client.GetComponentsAsync(stoppingToken).ConfigureAwait(false);
            foreach (var comp in initialComponents)
            {
                _registry.RegisterComponent(comp);
            }
            _logger.LogInformation("Successfully synced {Count} components from Control Plane", initialComponents.Count);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to perform initial component sync from Control Plane. Will retry in background stream.");
        }

        // 2. Start heartbeat background loop
        var heartbeatTask = RunHeartbeatLoopAsync(instanceId, stoppingToken);

        // 3. Start streaming listener loop
        var streamTask = RunStreamLoopAsync(instanceId, stoppingToken);

        await Task.WhenAll(heartbeatTask, streamTask).ConfigureAwait(false);
    }

    private async Task RunHeartbeatLoopAsync(string instanceId, CancellationToken stoppingToken)
    {
        var interval = _options.ControlPlane.HeartbeatInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _client.SendHeartbeatAsync(_options.AppId, instanceId, "Healthy", null, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Error sending heartbeat to Control Plane");
            }

            try
            {
                await Task.Delay(interval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunStreamLoopAsync(string instanceId, CancellationToken stoppingToken)
    {
        if (!_options.ControlPlane.EnableLiveSync)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Subscribing to real-time component stream for AppId {AppId}", _options.AppId);
                await foreach (var evt in _client.StreamUpdatesAsync(_options.AppId, instanceId, stoppingToken).ConfigureAwait(false))
                {
                    HandleSyncEvent(evt);
                }

                // If stream completed cleanly, wait briefly before reconnecting
                await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Component sync stream disconnected. Reconnecting in 3 seconds...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void HandleSyncEvent(ComponentSyncEventDto evt)
    {
        switch (evt.Action)
        {
            case ComponentSyncAction.Added:
            case ComponentSyncAction.Updated:
            case ComponentSyncAction.FullSync:
                if (evt.Definition is not null)
                {
                    _logger.LogInformation("Updating component {ComponentName} from Control Plane stream", evt.Definition.Name);
                    _registry.RegisterComponent(evt.Definition);
                }
                break;

            case ComponentSyncAction.Removed:
                if (!string.IsNullOrWhiteSpace(evt.ComponentName))
                {
                    _logger.LogInformation("Removing component {ComponentName} via Control Plane stream", evt.ComponentName);
                    _registry.RemoveComponent(evt.ComponentName);
                }
                break;
        }
    }
}
