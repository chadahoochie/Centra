using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// One live consumer: its channel, consumer tag, and in-flight tracker. Owns the shutdown order that
/// makes a drain meaningful - cancel the consumer, wait for the client to hand every already-prefetched
/// delivery to a handler, await those handlers, and only then close the channel.
/// </summary>
internal sealed class RabbitMQSubscription
{
    private readonly IChannel _channel;
    private readonly string _consumerTag;
    private readonly RabbitMQInFlightTracker _inFlight;
    private readonly TaskCompletionSource _consumerCancelled;

    public RabbitMQSubscription(
        IChannel channel,
        string consumerTag,
        RabbitMQInFlightTracker inFlight,
        TaskCompletionSource consumerCancelled)
    {
        _channel = channel;
        _consumerTag = consumerTag;
        _inFlight = inFlight;
        _consumerCancelled = consumerCancelled;
    }

    /// <summary>
    /// Cancels the consumer, waits for the client to finish dispatching the deliveries it had already
    /// read off the socket, drains the resulting in-flight handlers, and then closes and disposes the
    /// channel. <paramref name="drainTimeout"/> bounds only the handler drain - the waits for cancel-ok
    /// and for in-flight handlers - and is unbounded when it is <see cref="Timeout.InfiniteTimeSpan"/>.
    /// The basic.cancel RPC and the channel close are control-plane steps outside that budget, bounded
    /// by <paramref name="cancellationToken"/> and the client's own continuation timeout, so the
    /// consumer is always actually cancelled even when earlier subscriptions consumed the whole drain
    /// allowance. On the host shutdown path the host's token bounds them; on
    /// <see cref="RabbitMQPubSubDriver.DisposeAsync"/>, which passes
    /// <see cref="CancellationToken.None"/>, N subscriptions against an unresponsive broker cost N
    /// times the continuation timeout on top of the drain budget. Handlers left in flight are reported
    /// as an error, as is a consumer that could not be confirmed cancelled after this subscription had
    /// already received at least one delivery, or whose cancel RPC failed outright - in both of those
    /// the client may still hold buffered deliveries that the close drops. A consumer that was
    /// cancelled cleanly and never received a delivery has nothing to lose and is reported at no
    /// severity, however much of the drain budget was left. The channel closes either way, since
    /// holding it open indefinitely would wedge shutdown.
    /// </summary>
    public async ValueTask ShutdownAsync(TimeSpan drainTimeout, ILogger logger, CancellationToken cancellationToken)
    {
        var unbounded = drainTimeout == Timeout.InfiniteTimeSpan;
        var deadline = unbounded ? 0L : Environment.TickCount64 + (long)drainTimeout.TotalMilliseconds;

        var cancelRequested = true;
        try
        {
            await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error canceling consumer tag {Tag} during subscription shutdown", _consumerTag);
            cancelRequested = false;
        }

        // BasicCancelAsync only stops the broker from dispatching *new* messages. Deliveries the client
        // already buffered are still queued ahead of the cancel-ok on the consumer's serial dispatch
        // queue, so the in-flight counter is only trustworthy once cancel-ok has been observed.
        if (cancelRequested)
        {
            try
            {
                await _consumerCancelled.Task
                    .WaitAsync(
                        unbounded
                            ? Timeout.InfiniteTimeSpan
                            : TimeSpan.FromMilliseconds(Math.Max(0L, deadline - Environment.TickCount64)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }

        var cancelOkObserved = _consumerCancelled.Task.IsCompletedSuccessfully;

        var outcome = await _inFlight
            .WaitForDrainAsync(
                unbounded
                    ? Timeout.InfiniteTimeSpan
                    : TimeSpan.FromMilliseconds(Math.Max(0L, deadline - Environment.TickCount64)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!outcome.Drained)
        {
            logger.LogError(
                "Gave up draining RabbitMQ subscription {Tag} after {DrainTimeout}; {Outstanding} handler(s) were still in flight when the channel closed, so those messages will be redelivered",
                _consumerTag,
                drainTimeout,
                outcome.Outstanding);
        }
        else if (!cancelOkObserved && (_inFlight.HasReceivedDelivery || !cancelRequested))
        {
            logger.LogError(
                "Gave up draining RabbitMQ subscription {Tag} after {DrainTimeout}; the consumer was never confirmed cancelled, so any deliveries the client had already buffered are lost when the channel closes and will be redelivered",
                _consumerTag,
                drainTimeout);
        }

        try
        {
            await _channel.CloseAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error closing subscription channel during shutdown");
        }
        finally
        {
            _channel.Dispose();
        }
    }
}
