namespace Centra.Providers.RabbitMQ.Options;

public sealed class RabbitMQProviderOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "centra.pubsub";
    public string DefaultPubSubName { get; set; } = "pubsub";
    public string QueuePrefix { get; set; } = "centra";
}
