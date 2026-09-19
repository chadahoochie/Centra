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
    /// Total time budget for draining in-flight handlers across a whole shutdown, after each consumer
    /// has been cancelled and before its channel is closed anyway. It is an allowance for every
    /// subscription combined, not per subscription: a host tears subscriptions down one topic at a
    /// time, and both that path (via <c>CentraRuntimeHostedService.StopAsync</c>, which opens the
    /// driver's shutdown drain window first) and
    /// <see cref="PubSub.RabbitMQPubSubDriver.DisposeAsync"/> share one deadline, so the drain cost
    /// never scales with the number of topics. A lone
    /// <see cref="PubSub.RabbitMQPubSubDriver.UnsubscribeAsync"/> outside a shutdown drains one
    /// subscription and gets the whole allowance.
    /// Defaults to 10 seconds: comfortably longer than a typical handler, and the whole drain fits
    /// well inside the 30 second default host shutdown budget no matter how many topics are bound.
    /// Exceeding it is logged as an error, never silently ignored.
    /// Set <see cref="Timeout.InfiniteTimeSpan"/> to await in-flight handlers without any limit.
    /// Every other non-positive value is rejected by
    /// <see cref="RabbitMQProviderOptionsValidator"/> rather than silently degrading to no drain
    /// at all.
    /// </summary>
    public TimeSpan TotalShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
