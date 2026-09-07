namespace Centra.Sample.Resilience.Chaos;

public sealed class PaymentChaosState
{
    private int _totalRequestsReceived;
    private int _totalFailuresInjected;
    private int _totalSuccessesReturned;
    private int _transientBlipCount;

    public PaymentChaosMode Mode { get; set; } = PaymentChaosMode.Normal;

    public int TotalRequestsReceived => _totalRequestsReceived;
    public int TotalFailuresInjected => _totalFailuresInjected;
    public int TotalSuccessesReturned => _totalSuccessesReturned;

    public int IncrementRequests() => Interlocked.Increment(ref _totalRequestsReceived);
    public int IncrementFailures() => Interlocked.Increment(ref _totalFailuresInjected);
    public int IncrementSuccesses() => Interlocked.Increment(ref _totalSuccessesReturned);

    public int IncrementTransientBlips() => Interlocked.Increment(ref _transientBlipCount);
    public int TransientBlipCount => _transientBlipCount;

    public void ResetCounters()
    {
        Interlocked.Exchange(ref _totalRequestsReceived, 0);
        Interlocked.Exchange(ref _totalFailuresInjected, 0);
        Interlocked.Exchange(ref _totalSuccessesReturned, 0);
        Interlocked.Exchange(ref _transientBlipCount, 0);
    }
}
