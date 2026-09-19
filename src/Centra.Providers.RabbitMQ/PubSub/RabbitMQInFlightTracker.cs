namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// Counts the handler invocations currently in flight for one subscription and lets a shutdown path
/// wait for them to finish. <see cref="BeginDelivery"/> and <see cref="CompleteDelivery"/> are
/// interlocked-only so the per-message path stays allocation free; the completion signal is allocated
/// once, lazily, and only when a drain is actually requested.
/// </summary>
internal sealed class RabbitMQInFlightTracker
{
    private TaskCompletionSource? _drained;
    private int _inFlight;

    public void BeginDelivery() => Interlocked.Increment(ref _inFlight);

    public void CompleteDelivery()
    {
        if (Interlocked.Decrement(ref _inFlight) == 0)
        {
            Volatile.Read(ref _drained)?.TrySetResult();
        }
    }

    /// <summary>
    /// Waits until every in-flight handler has completed or <paramref name="timeout"/> elapses.
    /// A cancelled <paramref name="cancellationToken"/> ends the wait the same way a timeout does:
    /// the outcome reports the residual count so the caller can log it rather than having it
    /// disappear into an exception thrown from a shutdown path.
    /// </summary>
    public async ValueTask<RabbitMQDrainOutcome> WaitForDrainAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _inFlight) == 0)
        {
            return new RabbitMQDrainOutcome(true, 0);
        }

        if (timeout <= TimeSpan.Zero)
        {
            return new RabbitMQDrainOutcome(false, Volatile.Read(ref _inFlight));
        }

        var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var signal = Interlocked.CompareExchange(ref _drained, created, null) ?? created;

        // The last CompleteDelivery may have observed a null signal between the check above and the
        // exchange, so re-check rather than wait for a decrement that has already happened.
        if (Volatile.Read(ref _inFlight) == 0)
        {
            signal.TrySetResult();
        }

        try
        {
            await signal.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return new RabbitMQDrainOutcome(true, 0);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        return new RabbitMQDrainOutcome(false, Volatile.Read(ref _inFlight));
    }
}
