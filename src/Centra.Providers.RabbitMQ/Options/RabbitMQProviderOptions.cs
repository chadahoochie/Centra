namespace Centra.Providers.RabbitMQ.Options;

public sealed class RabbitMQProviderOptions
{
    public string? ConnectionString { get; set; }
    public Uri? Uri { get; set; }
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "centra.pubsub";
    public string DefaultPubSubName { get; set; } = "pubsub";
    public string QueuePrefix { get; set; } = "centra";

    /// <summary>
    /// Default prefetch count applied to consumer channels when not overridden by subscription options.
    /// Defaults to 50 for balanced throughput and memory utilization. Set to 0 for unlimited.
    /// </summary>
    public ushort DefaultPrefetchCount { get; set; } = 50;

    /// <summary>
    /// Default maximum number of concurrent message handler invocations allowed per consumer.
    /// Defaults to 1 (sequential message processing per channel).
    /// </summary>
    public int DefaultMaxConcurrentCalls { get; set; } = 1;

    /// <summary>
    /// Total time budget for waiting on in-flight handlers across a whole shutdown. It bounds the
    /// handler drain only: the basic.cancel RPC and the channel close are control-plane steps bounded
    /// by the caller's cancellation token and the client's own continuation timeout, so a consumer is
    /// always cancelled even once the drain allowance is spent.
    /// It is an allowance for every subscription combined, not per subscription: a host tears
    /// subscriptions down one topic at a time, and <c>CentraRuntimeHostedService.StopAsync</c> opens
    /// one shared drain window for the driver so the drain cost never scales with the number of
    /// topics. <see cref="PubSub.RabbitMQPubSubDriver.DisposeAsync"/> joins that window only while it
    /// is still open; a disposal after the host has closed it - which reaches any subscription created
    /// directly through <see cref="PubSub.RabbitMQPubSubDriver.SubscribeAsync"/> rather than through a
    /// router, since those survive the host stop - gets a fresh allowance, so a worst-case shutdown
    /// can spend this budget twice. A lone
    /// <see cref="PubSub.RabbitMQPubSubDriver.UnsubscribeAsync"/> outside a shutdown drains one
    /// subscription and gets the whole allowance.
    /// Defaults to 10 seconds: comfortably longer than a typical handler, and the whole drain fits
    /// well inside the 30 second default host shutdown budget no matter how many topics are bound.
    /// Handlers left in flight when it runs out are logged as an error, never silently ignored.
    /// Every configured value maps onto a defined behaviour, with no silent degradation and no
    /// shutdown-time exception: <see cref="Timeout.InfiniteTimeSpan"/>, any other negative duration,
    /// and any duration longer than the timer subsystem can wait (roughly 49.7 days) - which includes
    /// <see cref="TimeSpan.MaxValue"/> - all mean await in-flight handlers without any limit;
    /// <see cref="TimeSpan.Zero"/> means do not wait at all; every duration in between is used as-is.
    /// </summary>
    public TimeSpan TotalShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
