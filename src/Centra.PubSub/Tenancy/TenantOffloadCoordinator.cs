using System.Collections.Concurrent;
using Centra.Diagnostics;
using Centra.PubSub;

namespace Centra.PubSub.Tenancy;

public sealed class TenantOffloadCoordinator : ITenantOffloadCoordinator
{
    private readonly ITenantMetricsTracker _tracker;
    private readonly ITenantOffloadStrategy _strategy;
    private readonly TenantOffloadOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<TenantTopicKey, (TenantOffloadState State, TenantOffloadReason Reason, DateTimeOffset LastStateChange)> _tenantStates = new();

    public ITenantMetricsTracker Tracker => _tracker;

    public ITenantOffloadStrategy Strategy => _strategy;

    public TenantOffloadOptions Options => _options;

    public TimeProvider TimeProvider => _timeProvider;

    public TenantOffloadCoordinator(
        ITenantMetricsTracker tracker,
        ITenantOffloadStrategy strategy,
        TenantOffloadOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _options = options ?? new TenantOffloadOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsTenantOffloaded(string tenantId, string topic, out TenantOffloadReason reason)
    {
        reason = TenantOffloadReason.None;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        var key = new TenantTopicKey(topic, tenantId);
        if (_tenantStates.TryGetValue(key, out var state))
        {
            if (state.State == TenantOffloadState.Offloaded)
            {
                if (_tracker.ShouldOffload(tenantId, topic, out var currentReason))
                {
                    reason = currentReason;
                    return true;
                }

                if (_timeProvider.GetUtcNow() - state.LastStateChange >= _options.CooldownPeriod)
                {
                    _tenantStates[key] = (TenantOffloadState.Normal, TenantOffloadReason.None, _timeProvider.GetUtcNow());
                    CentraMeters.RecordTenantStateTransition(tenantId, topic, "Offloaded", "Normal", "CooldownExpired");
                    return false;
                }

                reason = state.Reason;
                return true;
            }
        }
        else
        {
            if (_tracker.ShouldOffload(tenantId, topic, out var evalReason))
            {
                _tenantStates[key] = (TenantOffloadState.Offloaded, evalReason, _timeProvider.GetUtcNow());
                CentraMeters.RecordTenantStateTransition(tenantId, topic, "Normal", "Offloaded", evalReason.ToString());
                reason = evalReason;
                return true;
            }
        }

        return false;
    }

    public void ForceOffload(string tenantId, string topic, TenantOffloadReason reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var key = new TenantTopicKey(topic, tenantId);
        _tenantStates[key] = (TenantOffloadState.Offloaded, reason, _timeProvider.GetUtcNow());
        CentraMeters.RecordTenantStateTransition(tenantId, topic, "Normal", "Offloaded", reason.ToString());
    }

    public void ClearOffload(string tenantId, string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var key = new TenantTopicKey(topic, tenantId);
        _tenantStates[key] = (TenantOffloadState.Normal, TenantOffloadReason.None, _timeProvider.GetUtcNow());
        CentraMeters.RecordTenantStateTransition(tenantId, topic, "Offloaded", "Normal", "ManualClear");
    }

    public async ValueTask<EventHandlingResult> HandleOffloadAsync(TenantOffloadWorkItem workItem, CancellationToken cancellationToken)
    {
        var result = await _strategy.ExecuteOffloadAsync(workItem, cancellationToken).ConfigureAwait(false);
        CentraMeters.RecordTenantOffloaded(workItem.TenantId, workItem.Topic, _strategy.StrategyType.ToString(), "DynamicOffload");
        return result;
    }

    public void RecordExecution(string tenantId, string topic, double durationMs)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        _tracker.RecordExecution(tenantId, topic, durationMs);

        var key = new TenantTopicKey(topic, tenantId);
        if (!_tenantStates.TryGetValue(key, out var state) || state.State == TenantOffloadState.Normal)
        {
            if (_tracker.ShouldOffload(tenantId, topic, out var reason))
            {
                _tenantStates[key] = (TenantOffloadState.Offloaded, reason, _timeProvider.GetUtcNow());
                CentraMeters.RecordTenantStateTransition(tenantId, topic, "Normal", "Offloaded", reason.ToString());
            }
        }

        var stats = _tracker.GetTenantStats(tenantId, topic);
        CentraMeters.RecordTenantMetrics(tenantId, topic, stats.TrafficShareRatio, durationMs);
    }

    public string ResolvePublishTopic(string pubSubName, string baseTopic, string? tenantId)
    {
        if (!_options.EnablePublisherBypassing || string.IsNullOrWhiteSpace(tenantId))
        {
            return baseTopic;
        }

        if (IsTenantOffloaded(tenantId, baseTopic, out _))
        {
            return _strategy.ResolvePublishTopic(baseTopic, tenantId);
        }

        return baseTopic;
    }

    public async ValueTask CleanupIdleResourcesAsync(CancellationToken cancellationToken)
    {
        await _strategy.CleanupIdleResourcesAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string BuildKey(string topic, string tenantId) => $"{topic}:{tenantId}";
}
