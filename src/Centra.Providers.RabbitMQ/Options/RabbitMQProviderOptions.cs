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
    /// Time budget for draining in-flight handlers after a consumer has been cancelled and before its
    /// channel is closed anyway. <see cref="PubSub.RabbitMQPubSubDriver.UnsubscribeAsync"/> tears down a
    /// single subscription, so it gets the whole budget.
    /// <see cref="PubSub.RabbitMQPubSubDriver.DisposeAsync"/> tears down every subscription
    /// sequentially and shares one budget across all of them, so disposal never costs
    /// subscription-count multiples of this value. When the host drains topic by topic (via
    /// <c>CentraRuntimeHostedService.StopAsync</c>) each subscription gets its own budget, and the
    /// overall bound is the shutdown <see cref="System.Threading.CancellationToken"/> the host
    /// supplies - cancelling it ends the drain immediately.
    /// Defaults to 10 seconds: comfortably longer than a typical handler, and a third of the 30 second
    /// default host shutdown budget, so a handful of topics still drain within it.
    /// Exceeding it is logged as an error, never silently ignored.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
