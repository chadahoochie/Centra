using Azure.Messaging.ServiceBus;
using Centra.PubSub;

namespace Centra.Providers.AzureServiceBus.PubSub;

/// <summary>
/// Defines a contract for settling Azure Service Bus messages according to the subscriber's <see cref="EventHandlingResult"/>.
/// </summary>
public interface IServiceBusMessageSettler
{
    /// <summary>
    /// Settles a standard topic message based on the event handling outcome.
    /// </summary>
    Task SettleMessageAsync(ProcessMessageEventArgs args, EventHandlingResult result);

    /// <summary>
    /// Settles a session-enabled topic message based on the event handling outcome.
    /// </summary>
    Task SettleSessionMessageAsync(ProcessSessionMessageEventArgs args, EventHandlingResult result);
}
