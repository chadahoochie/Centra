using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// One live consumer: its channel, consumer tag, optional concurrency limiter, and in-flight tracker.
/// Owns the shutdown order that makes a drain meaningful - cancel the consumer so the broker stops
/// dispatching, await the handlers already running, and only then close the channel.
/// </summary>
internal sealed class RabbitMQSubscription
{
    public RabbitMQSubscription(IChannel channel, string consumerTag, SemaphoreSlim? limiter, RabbitMQInFlightTracker inFlight)
    {
        Channel = channel;
        ConsumerTag = consumerTag;
        Limiter = limiter;
        InFlight = inFlight;
    }

    public IChannel Channel { get; }

    public string ConsumerTag { get; }

    public SemaphoreSlim? Limiter { get; }

    public RabbitMQInFlightTracker InFlight { get; }

    /// <summary>
    /// Cancels the consumer, drains in-flight handlers for up to <paramref name="drainTimeout"/>,
    /// then closes and disposes the channel. A drain that does not finish in time is reported as an
    /// error - the channel still closes, because holding it open indefinitely would wedge shutdown.
    /// </summary>
    public async ValueTask ShutdownAsync(TimeSpan drainTimeout, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await Channel.BasicCancelAsync(ConsumerTag, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error canceling consumer tag {Tag} during subscription shutdown", ConsumerTag);
        }

        var outstanding = await InFlight.WaitForDrainAsync(drainTimeout, cancellationToken).ConfigureAwait(false);
        if (outstanding > 0)
        {
            logger.LogError(
                "Gave up draining RabbitMQ subscription {Tag} after {DrainTimeout}; {Outstanding} handler(s) were still in flight when the channel closed, so those messages will be redelivered",
                ConsumerTag,
                drainTimeout,
                outstanding);
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

        Limiter?.Dispose();
    }
}
