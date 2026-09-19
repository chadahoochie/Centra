using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// Defines a contract for resolving the stable identity a redelivered message keeps across requeues,
/// which is what a consumer-side retry budget must be keyed on.
/// </summary>
public interface IRabbitMQMessageIdentityReader
{
    /// <summary>
    /// Resolves the message identity, or <see langword="null"/> when the delivery carries no identity that
    /// survives redelivery.
    /// </summary>
    string? ResolveIdentity(IReadOnlyDictionary<string, string>? headers, IReadOnlyBasicProperties? properties);
}
