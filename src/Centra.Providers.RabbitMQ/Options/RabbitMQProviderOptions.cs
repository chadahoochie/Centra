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
    /// Default number of redeliveries granted to a handler returning <c>Retry</c> before the message is
    /// dead-lettered, when not overridden by subscription options. Defaults to 3, so a failing message is
    /// handed to the handler at most 4 times in total. Set to 0 to dead-letter on first failure.
    /// </summary>
    /// <remarks>
    /// The budget is enforced by the consumer. RabbitMQ only advances <c>x-delivery-count</c> when a delivery
    /// is returned by consumer or channel failure, never on an application <c>basic.nack(requeue=true)</c>, so
    /// <c>x-delivery-limit</c> cannot bound this loop and is only a backstop against channel-failure loops.
    /// </remarks>
    public int DefaultMaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Default delay before the first redelivery, doubled on each subsequent retry and clamped to
    /// <see cref="DefaultRetryMaxBackoff"/>, when not overridden by subscription options. Defaults to 1 second.
    /// </summary>
    public TimeSpan DefaultRetryInitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Default ceiling for the exponentially growing redelivery backoff, when not overridden by subscription
    /// options. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan DefaultRetryMaxBackoff { get; set; } = TimeSpan.FromSeconds(30);
}
