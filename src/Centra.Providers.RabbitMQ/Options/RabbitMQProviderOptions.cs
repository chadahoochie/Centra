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
    /// Maximum time <see cref="PubSub.RabbitMQPubSubDriver.UnsubscribeAsync"/> and
    /// <see cref="PubSub.RabbitMQPubSubDriver.DisposeAsync"/> wait for in-flight handlers to finish
    /// after the consumer has been cancelled, before the subscription channel is closed anyway.
    /// Defaults to 10 seconds: comfortably longer than a typical handler, and short enough that
    /// several subscriptions can still drain inside the 30 second default host shutdown budget.
    /// Exceeding it is logged as an error, never silently ignored.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
