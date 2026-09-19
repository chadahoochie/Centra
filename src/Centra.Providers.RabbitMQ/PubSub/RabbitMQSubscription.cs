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
    private readonly TaskCompletionSource _consumerCancelled;

    public RabbitMQSubscription(
        IChannel channel,
        string consumerTag,
        SemaphoreSlim? limiter,
        RabbitMQInFlightTracker inFlight,
        TaskCompletionSource consumerCancelled)
    {
        Channel = channel;
        ConsumerTag = consumerTag;
        Limiter = limiter;
        InFlight = inFlight;
        _consumerCancelled = consumerCancelled;
    }

    public IChannel Channel { get; }

    public string ConsumerTag { get; }

    public SemaphoreSlim? Limiter { get; }

    public RabbitMQInFlightTracker InFlight { get; }

    /// <summary>
    /// Cancels the consumer, waits for the client to finish dispatching the deliveries it had already
    /// read off the socket, drains the resulting in-flight handlers, and then closes and disposes the
    /// channel - all inside a single <paramref name="drainTimeout"/> budget. A drain that does not
    /// finish in time is reported as an error; the channel still closes, because holding it open
    /// indefinitely would wedge shutdown. The concurrency limiter is only disposed once the drain
    /// completed, so a handler that outlived the budget cannot observe a disposed limiter.
    /// </summary>
    public async ValueTask ShutdownAsync(TimeSpan drainTimeout, ILogger logger, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)Math.Max(0d, drainTimeout.TotalMilliseconds);

        try
        {
            await Channel.BasicCancelAsync(ConsumerTag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error canceling consumer tag {Tag} during subscription shutdown", ConsumerTag);
            _consumerCancelled.TrySetResult();
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

        var outcome = await InFlight
            .WaitForDrainAsync(TimeSpan.FromMilliseconds(Math.Max(0L, deadline - Environment.TickCount64)), cancellationToken)
            .ConfigureAwait(false);

        if (!outcome.Drained)
        {
            logger.LogError(
                "Gave up draining RabbitMQ subscription {Tag} after {DrainTimeout}; {Outstanding} handler(s) were still in flight when the channel closed, so those messages will be redelivered",
                ConsumerTag,
                drainTimeout,
                outcome.Outstanding);
        }

        try
        {
            await Channel.CloseAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            Channel.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error closing subscription channel during shutdown");
        }

        if (outcome.Drained)
        {
            Limiter?.Dispose();
        }
    }
}
