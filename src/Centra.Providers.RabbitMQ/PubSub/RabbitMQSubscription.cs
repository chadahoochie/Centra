using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// One live consumer: its channel, consumer tag, optional concurrency limiter, and in-flight tracker.
/// Owns the shutdown order that makes a drain meaningful - cancel the consumer, wait for the client to
/// hand every already-prefetched delivery to a handler, await those handlers, and only then close the
/// channel.
/// </summary>
internal sealed class RabbitMQSubscription
{
    private readonly IChannel _channel;
    private readonly string _consumerTag;
    private readonly SemaphoreSlim? _limiter;
    private readonly RabbitMQInFlightTracker _inFlight;
    private readonly TaskCompletionSource _consumerCancelled;

    public RabbitMQSubscription(
        IChannel channel,
        string consumerTag,
        SemaphoreSlim? limiter,
        RabbitMQInFlightTracker inFlight,
        TaskCompletionSource consumerCancelled)
    {
        _channel = channel;
        _consumerTag = consumerTag;
        _limiter = limiter;
        _inFlight = inFlight;
        _consumerCancelled = consumerCancelled;
    }

    /// <summary>
    /// Cancels the consumer, waits for the client to finish dispatching the deliveries it had already
    /// read off the socket, drains the resulting in-flight handlers, and then closes and disposes the
    /// channel. Every wait - including the basic.cancel RPC, which would otherwise be bounded only by
    /// the client's own continuation timeout - draws from the single <paramref name="drainTimeout"/>
    /// budget, so this method cannot outlive it by more than the channel close. A drain that does not
    /// finish in time is reported as an error; the channel still closes, because holding it open
    /// indefinitely would wedge shutdown. The concurrency limiter is only disposed once the drain
    /// completed, so a handler that outlived the budget cannot observe a disposed limiter.
    /// </summary>
    public async ValueTask ShutdownAsync(TimeSpan drainTimeout, ILogger logger, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)Math.Max(0d, drainTimeout.TotalMilliseconds);

        using (var cancelRpc = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cancelRpc.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(0L, deadline - Environment.TickCount64)));
            try
            {
                await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancelRpc.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error canceling consumer tag {Tag} during subscription shutdown", _consumerTag);
                _consumerCancelled.TrySetResult();
            }
        }

        // BasicCancelAsync only stops the broker from dispatching *new* messages. Deliveries the client
        // already buffered are still queued ahead of the cancel-ok on the consumer's serial dispatch
        // queue, so the in-flight counter is only trustworthy once cancel-ok has been observed.
        try
        {
            await _consumerCancelled.Task
                .WaitAsync(TimeSpan.FromMilliseconds(Math.Max(0L, deadline - Environment.TickCount64)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        var outcome = await _inFlight
            .WaitForDrainAsync(TimeSpan.FromMilliseconds(Math.Max(0L, deadline - Environment.TickCount64)), cancellationToken)
            .ConfigureAwait(false);

        if (!outcome.Drained)
        {
            logger.LogError(
                "Gave up draining RabbitMQ subscription {Tag} after {DrainTimeout}; {Outstanding} handler(s) were still in flight when the channel closed, so those messages will be redelivered",
                _consumerTag,
                drainTimeout,
                outcome.Outstanding);
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

        if (outcome.Drained)
        {
            _limiter?.Dispose();
        }
    }
}
