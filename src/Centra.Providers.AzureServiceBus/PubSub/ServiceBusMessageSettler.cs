using Azure.Messaging.ServiceBus;
using Centra.PubSub;

namespace Centra.Providers.AzureServiceBus.PubSub;

/// <summary>
/// Default implementation of <see cref="IServiceBusMessageSettler"/>.
/// </summary>
public sealed class ServiceBusMessageSettler : IServiceBusMessageSettler
{
    /// <summary>
    /// Singleton default instance of <see cref="ServiceBusMessageSettler"/>.
    /// </summary>
    public static readonly ServiceBusMessageSettler Instance = new();

    public Task SettleMessageAsync(ProcessMessageEventArgs args, EventHandlingResult result)
    {
        ArgumentNullException.ThrowIfNull(args);

        return result switch
        {
            EventHandlingResult.Success or EventHandlingResult.Drop =>
                args.CompleteMessageAsync(args.Message, args.CancellationToken),

            EventHandlingResult.DeadLetter =>
                args.DeadLetterMessageAsync(args.Message, "DeadLetter", "Handler requested dead-lettering", args.CancellationToken),

            _ => args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken)
        };
    }

    public Task SettleSessionMessageAsync(ProcessSessionMessageEventArgs args, EventHandlingResult result)
    {
        ArgumentNullException.ThrowIfNull(args);

        return result switch
        {
            EventHandlingResult.Success or EventHandlingResult.Drop =>
                args.CompleteMessageAsync(args.Message, args.CancellationToken),

            EventHandlingResult.DeadLetter =>
                args.DeadLetterMessageAsync(args.Message, "DeadLetter", "Handler requested dead-lettering", args.CancellationToken),

            _ => args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken)
        };
    }
}
