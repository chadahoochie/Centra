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
}
