using System.Collections.Concurrent;

namespace Centra.PubSub.Tenancy;

public sealed class RollingWindowTenantMetricsTracker : ITenantMetricsTracker
{
    private static readonly Func<TenantTopicKey, (int BucketCount, TimeProvider TimeProvider), TenantTopicRollingWindow> WindowFactory =
        static (_, state) => new TenantTopicRollingWindow(state.BucketCount, state.TimeProvider);

    private readonly TenantOffloadOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<TenantTopicKey, TenantTopicRollingWindow> _windows = new();
    private readonly int _bucketCount;

    public RollingWindowTenantMetricsTracker(TenantOffloadOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new TenantOffloadOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _bucketCount = Math.Max(1, (int)_options.WindowDuration.TotalSeconds);
    }

    public void RecordExecution(string tenantId, string topic, double durationMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var tenantKey = new TenantTopicKey(topic, tenantId);
        var overallKey = new TenantTopicKey(topic, TenantTopicKey.OverallTenantId);

        var tenantWindow = _windows.GetOrAdd(tenantKey, WindowFactory, (_bucketCount, _timeProvider));
        var overallWindow = _windows.GetOrAdd(overallKey, WindowFactory, (_bucketCount, _timeProvider));

        tenantWindow.Record(durationMs);
        overallWindow.Record(durationMs);
    }

    public TenantTrafficStats GetTenantStats(string tenantId, string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var now = _timeProvider.GetUtcNow();
        var nowSeconds = now.ToUnixTimeSeconds();
        var windowSeconds = (long)_options.WindowDuration.TotalSeconds;

        var tenantKey = new TenantTopicKey(topic, tenantId);
        var overallKey = new TenantTopicKey(topic, TenantTopicKey.OverallTenantId);

        _windows.TryGetValue(tenantKey, out var tenantWindow);
        _windows.TryGetValue(overallKey, out var overallWindow);

        var (tenantMessages, tenantDurationMs) = tenantWindow?.GetWindowTotals(nowSeconds, windowSeconds) ?? (0, 0.0);
        var (overallMessages, overallDurationMs) = overallWindow?.GetWindowTotals(nowSeconds, windowSeconds) ?? (0, 0.0);

        var trafficShareRatio = overallMessages > 0 ? (double)tenantMessages / overallMessages : 0.0;
        var avgDuration = tenantMessages > 0 ? tenantDurationMs / tenantMessages : 0.0;
        var overallAvg = overallMessages > 0 ? overallDurationMs / overallMessages : 0.0;
        var durationRatio = overallAvg > 0 ? avgDuration / overallAvg : 1.0;

        var state = tenantWindow?.CurrentState ?? TenantOffloadState.Normal;
        var reason = tenantWindow?.CurrentReason ?? TenantOffloadReason.None;

        return new TenantTrafficStats(
            TenantId: tenantId,
            MessageCount: tenantMessages,
            TrafficShareRatio: trafficShareRatio,
            AverageDurationMs: avgDuration,
            DurationRatio: durationRatio,
            State: state,
            Reason: reason);
    }

    public IReadOnlyList<TenantTrafficStats> GetAllTenantStats(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var result = new List<TenantTrafficStats>();

        foreach (var kvp in _windows)
        {
            if (string.Equals(kvp.Key.Topic, topic, StringComparison.Ordinal) &&
                !string.Equals(kvp.Key.TenantId, TenantTopicKey.OverallTenantId, StringComparison.Ordinal))
            {
                result.Add(GetTenantStats(kvp.Key.TenantId, topic));
            }
        }

        return result;
    }

    public bool ShouldOffload(string tenantId, string topic, out TenantOffloadReason reason)
    {
        var stats = GetTenantStats(tenantId, topic);
        reason = TenantOffloadReason.None;

        if (stats.MessageCount < _options.MinSampleCount)
        {
            return false;
        }

        if (stats.TrafficShareRatio >= _options.TrafficShareThreshold)
        {
            reason |= TenantOffloadReason.HighTrafficShare;
        }

        if (stats.DurationRatio >= _options.DurationMultiplierThreshold)
        {
            reason |= TenantOffloadReason.DisproportionateOperationDuration;
        }

        if (_options.DurationAbsoluteThresholdMs.HasValue && stats.AverageDurationMs >= _options.DurationAbsoluteThresholdMs.Value)
        {
            reason |= TenantOffloadReason.DisproportionateOperationDuration;
        }

        return reason != TenantOffloadReason.None;
    }

    public void Reset(string? tenantId = null)
    {
        if (tenantId is null)
        {
            foreach (var window in _windows.Values)
            {
                window.Reset();
            }
            _windows.Clear();
        }
        else
        {
            foreach (var kvp in _windows)
            {
                if (string.Equals(kvp.Key.TenantId, tenantId, StringComparison.Ordinal))
                {
                    kvp.Value.Reset();
                    _windows.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    internal TenantTopicRollingWindow GetOrCreateWindow(string tenantId, string topic)
    {
        var key = new TenantTopicKey(topic, tenantId);
        return _windows.GetOrAdd(key, WindowFactory, (_bucketCount, _timeProvider));
    }

    internal static string BuildKey(string topic, string tenantId) => $"{topic}:{tenantId}";
}
