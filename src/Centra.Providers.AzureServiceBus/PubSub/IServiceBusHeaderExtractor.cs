using Azure.Messaging.ServiceBus;

namespace Centra.Providers.AzureServiceBus.PubSub;

/// <summary>
/// Defines a contract for extracting and mapping CloudEvents headers from an incoming Azure Service Bus message.
/// </summary>
public interface IServiceBusHeaderExtractor
{
    /// <summary>
    /// Extracts CloudEvents and application property headers from the specified Service Bus message.
    /// </summary>
    IReadOnlyDictionary<string, string> ExtractHeaders(ServiceBusReceivedMessage message);
}
