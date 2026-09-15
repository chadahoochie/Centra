namespace Centra.PubSub.Tenancy;

public sealed class TenantTopicRollingWindow
{
    private readonly TenantWindowBucket[] _buckets;
    private readonly int _bucketCount;
    private readonly TimeProvider _timeProvider;
    private long _lastStateChangeEpochMs;
    private long _lastActivityEpochMs;

    public TenantOffloadState CurrentState { get; set; } = TenantOffloadState.Normal;

    public TenantOffloadReason CurrentReason { get; set; } = TenantOffloadReason.None;

    public DateTimeOffset LastStateChangeTime
    {
        get => DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref _lastStateChangeEpochMs));
        set => Interlocked.Exchange(ref _lastStateChangeEpochMs, value.ToUnixTimeMilliseconds());
    }

    public DateTimeOffset LastActivityTime
    {
        get => DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref _lastActivityEpochMs));
        set => Interlocked.Exchange(ref _lastActivityEpochMs, value.ToUnixTimeMilliseconds());
    }

    public TenantTopicRollingWindow(int bucketCount = 60, TimeProvider? timeProvider = null)
    {
        _bucketCount = Math.Max(1, bucketCount);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _buckets = new TenantWindowBucket[_bucketCount];
        for (var i = 0; i < _bucketCount; i++)
        {
            _buckets[i] = new TenantWindowBucket();
        }
        var now = _timeProvider.GetUtcNow();
        LastStateChangeTime = now;
        LastActivityTime = now;
    }

    public void Record(double durationMs)
    {
        var now = _timeProvider.GetUtcNow();
        var nowSeconds = now.ToUnixTimeSeconds();
        LastActivityTime = now;

        var bucketIndex = (int)(Math.Abs(nowSeconds) % _bucketCount);
        var bucket = _buckets[bucketIndex];

        bucket.Record(nowSeconds, (long)(durationMs * 1000.0));
    }

    public (long TotalMessages, double TotalDurationMs) GetWindowTotals(long currentSeconds, long windowSeconds)
    {
        long totalMessages = 0;
        long totalDurationMicroseconds = 0;

        for (var i = 0; i < _bucketCount; i++)
        {
            var bucket = _buckets[i];
            var bucketTimestamp = bucket.TimestampSeconds;
            if (bucketTimestamp > 0 && (currentSeconds - bucketTimestamp) >= 0 && (currentSeconds - bucketTimestamp) < windowSeconds)
            {
                totalMessages += bucket.MessageCount;
                totalDurationMicroseconds += bucket.TotalDurationMicroseconds;
            }
        }

        return (totalMessages, totalDurationMicroseconds / 1000.0);
    }

    public void Reset()
    {
        for (var i = 0; i < _bucketCount; i++)
        {
            _buckets[i].Reset(0);
        }
        CurrentState = TenantOffloadState.Normal;
        CurrentReason = TenantOffloadReason.None;
        var now = _timeProvider.GetUtcNow();
        LastStateChangeTime = now;
        LastActivityTime = now;
    }
}
