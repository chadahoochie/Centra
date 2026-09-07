using Centra.Components;
using Centra.Hosting.Options;
using Centra.Resilience;
using Centra.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.HostedServices;

public sealed class CentraControlPlaneSyncHostedService : BackgroundService
{
    private readonly IControlPlaneClient _client;
    private readonly IComponentRegistry _registry;
    private readonly IResiliencePolicyRegistry? _resilienceRegistry;
    private readonly CentraOptions _options;
    private readonly ILogger<CentraControlPlaneSyncHostedService> _logger;
    private readonly TimeProvider _timeProvider;

    public CentraControlPlaneSyncHostedService(
        IControlPlaneClient client,
        IComponentRegistry registry,
        IOptions<CentraOptions> options,
        ILogger<CentraControlPlaneSyncHostedService> logger,
        TimeProvider? timeProvider = null,
        IResiliencePolicyRegistry? resilienceRegistry = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _resilienceRegistry = resilienceRegistry;
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

        // 1. Initial full fetch of components
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

        // 2. Initial full fetch of resilience policies
        if (_resilienceRegistry is not null)
        {
            try
            {
                _logger.LogInformation("Performing initial resilience policy sync with Control Plane at {Endpoint}", endpoint);
                var initialPolicies = await _client.GetResiliencePoliciesAsync(stoppingToken).ConfigureAwait(false);
                foreach (var policyDto in initialPolicies)
                {
                    var policyDef = MapFromDto(policyDto);
                    _resilienceRegistry.RegisterPolicy(policyDef);
                }
                _logger.LogInformation("Successfully synced {Count} resilience policies from Control Plane", initialPolicies.Count);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to perform initial resilience policy sync from Control Plane.");
            }
        }

        // 3. Start heartbeat background loop
        var heartbeatTask = RunHeartbeatLoopAsync(instanceId, stoppingToken);

        // 4. Start streaming listener loops
        var streamTask = RunStreamLoopAsync(instanceId, stoppingToken);
        var resilienceStreamTask = _resilienceRegistry != null ? RunResilienceStreamLoopAsync(instanceId, stoppingToken) : Task.CompletedTask;

        await Task.WhenAll(heartbeatTask, streamTask, resilienceStreamTask).ConfigureAwait(false);
    }

    private async Task RunHeartbeatLoopAsync(string instanceId, CancellationToken stoppingToken)
    {
        var interval = _options.ControlPlane.HeartbeatInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var metadata = _options.ControlPlane.Metadata.Count > 0 ? _options.ControlPlane.Metadata : null;
                await _client.SendHeartbeatAsync(_options.AppId, instanceId, "Healthy", metadata, stoppingToken).ConfigureAwait(false);
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

    private async Task RunResilienceStreamLoopAsync(string instanceId, CancellationToken stoppingToken)
    {
        if (!_options.ControlPlane.EnableLiveSync || _resilienceRegistry is null)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Subscribing to real-time resilience stream for AppId {AppId}", _options.AppId);
                await foreach (var evt in _client.StreamResilienceUpdatesAsync(_options.AppId, instanceId, stoppingToken).ConfigureAwait(false))
                {
                    HandleResilienceSyncEvent(evt);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Resilience sync stream disconnected. Reconnecting in 3 seconds...");
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

    private void HandleResilienceSyncEvent(ResilienceSyncEventDto evt)
    {
        if (_resilienceRegistry is null)
        {
            return;
        }

        if (string.Equals(evt.Action, "Deleted", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Removing resilience policy {PolicyName} via Control Plane stream", evt.PolicyName);
            _resilienceRegistry.RemovePolicy(evt.PolicyName);
        }
        else if (evt.Policy is not null)
        {
            _logger.LogInformation("Updating resilience policy {PolicyName} from Control Plane stream", evt.Policy.PolicyName);
            var def = MapFromDto(evt.Policy);
            _resilienceRegistry.RegisterPolicy(def);
        }
    }

    private static CentraResiliencePolicyDefinition MapFromDto(ResiliencePolicyDto dto)
    {
        RetryPolicyOptions? retry = null;
        if (dto.MaxRetries.HasValue)
        {
            var backoff = dto.BackoffType switch
            {
                "Constant" => CentraBackoffType.Constant,
                "Linear" => CentraBackoffType.Linear,
                _ => CentraBackoffType.Exponential
            };

            retry = new RetryPolicyOptions(
                MaxRetries: dto.MaxRetries.Value,
                BackoffType: backoff,
                BaseDelay: dto.BaseDelayMs.HasValue ? TimeSpan.FromMilliseconds(dto.BaseDelayMs.Value) : TimeSpan.FromMilliseconds(100),
                MaxDelay: dto.MaxDelayMs.HasValue ? TimeSpan.FromMilliseconds(dto.MaxDelayMs.Value) : TimeSpan.FromSeconds(2),
                UseJitter: dto.UseJitter ?? true);
        }

        CircuitBreakerPolicyOptions? circuitBreaker = null;
        if (dto.FailureRatio.HasValue || dto.BreakDurationSeconds.HasValue)
        {
            circuitBreaker = new CircuitBreakerPolicyOptions(
                FailureRatio: dto.FailureRatio ?? 0.5,
                SamplingDuration: dto.SamplingDurationSeconds.HasValue ? TimeSpan.FromSeconds(dto.SamplingDurationSeconds.Value) : TimeSpan.FromSeconds(10),
                MinimumThroughput: dto.MinimumThroughput ?? 5,
                BreakDuration: dto.BreakDurationSeconds.HasValue ? TimeSpan.FromSeconds(dto.BreakDurationSeconds.Value) : TimeSpan.FromSeconds(5));
        }

        TimeoutPolicyOptions? timeout = null;
        if (dto.TimeoutSeconds.HasValue)
        {
            timeout = new TimeoutPolicyOptions(TimeSpan.FromSeconds(dto.TimeoutSeconds.Value));
        }

        RateLimiterPolicyOptions? rateLimiter = null;
        if (dto.PermitLimit.HasValue)
        {
            rateLimiter = new RateLimiterPolicyOptions(
                PermitLimit: dto.PermitLimit.Value,
                QueueLimit: dto.QueueLimit ?? 10,
                Window: dto.WindowSeconds.HasValue ? TimeSpan.FromSeconds(dto.WindowSeconds.Value) : TimeSpan.FromSeconds(1));
        }

        BulkheadPolicyOptions? bulkhead = null;
        if (dto.MaxParallelism.HasValue)
        {
            bulkhead = new BulkheadPolicyOptions(
                MaxParallelism: dto.MaxParallelism.Value,
                MaxQueuedActions: dto.MaxQueuedActions ?? 20);
        }

        return new CentraResiliencePolicyDefinition(
            PolicyName: dto.PolicyName,
            Retry: retry,
            CircuitBreaker: circuitBreaker,
            Timeout: timeout,
            RateLimiter: rateLimiter,
            Bulkhead: bulkhead);
    }
}
