namespace Centra.Providers.AzureServiceBus.Options;

public sealed class AzureServiceBusProviderOptions
{
    public string ConnectionString { get; set; } = "Endpoint=sb://localhost.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake";
    public string DefaultPubSubName { get; set; } = "pubsub";
    public string TopicPrefix { get; set; } = "";
    public string SubscriptionName { get; set; } = "centra-sub";
    public int MaxConcurrentCalls { get; set; } = 1;
    public bool AutoCompleteMessages { get; set; } = false;
}
