namespace Centra.PubSub.Tenancy;

public sealed class TenantWindowBucket
{
    private readonly object _syncLock = new();
    private long _timestampSeconds;
    private long _messageCount;
    private long _totalDurationMicroseconds;

    public long TimestampSeconds => Interlocked.Read(ref _timestampSeconds);

    public long MessageCount => Interlocked.Read(ref _messageCount);

    public long TotalDurationMicroseconds => Interlocked.Read(ref _totalDurationMicroseconds);

    public void Record(long timestampSeconds, long durationMicroseconds)
    {
        if (Interlocked.Read(ref _timestampSeconds) != timestampSeconds)
        {
            lock (_syncLock)
            {
                if (Interlocked.Read(ref _timestampSeconds) != timestampSeconds)
                {
                    Interlocked.Exchange(ref _messageCount, 0);
                    Interlocked.Exchange(ref _totalDurationMicroseconds, 0);
                    Interlocked.Exchange(ref _timestampSeconds, timestampSeconds);
                }
            }
        }

        Interlocked.Increment(ref _messageCount);
        Interlocked.Add(ref _totalDurationMicroseconds, durationMicroseconds);
    }

    public void Reset(long timestampSeconds = 0)
    {
        lock (_syncLock)
        {
            Interlocked.Exchange(ref _messageCount, 0);
            Interlocked.Exchange(ref _totalDurationMicroseconds, 0);
            Interlocked.Exchange(ref _timestampSeconds, timestampSeconds);
        }
    }
}
