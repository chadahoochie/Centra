namespace Centra.Providers.AzureServiceBus.Options;

public sealed class AzureServiceBusProviderOptions
{
    public string ConnectionString { get; set; } = "Endpoint=sb://localhost.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake";
    public string DefaultPubSubName { get; set; } = "pubsub";
    public string TopicPrefix { get; set; } = "";
    public string SubscriptionName { get; set; } = "centra-sub";
    public int MaxConcurrentCalls { get; set; } = 1;
    public int PrefetchCount { get; set; } = 0;
    public bool AutoCompleteMessages { get; set; } = false;

    /// <summary>
    /// Session id used for ConsumerMode.SingleActiveConsumer subscriptions. The target
    /// subscription must be provisioned with RequiresSession=true for this to take effect -
    /// that is an infrastructure/provisioning concern this driver cannot change at runtime.
    /// </summary>
    public string SingleActiveSessionId { get; set; } = "centra-single-active";
}
