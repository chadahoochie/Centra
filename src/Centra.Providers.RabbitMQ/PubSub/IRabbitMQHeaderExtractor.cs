using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// Defines a contract for extracting headers from incoming RabbitMQ basic properties.
/// </summary>
public interface IRabbitMQHeaderExtractor
{
    /// <summary>
    /// Extracts headers from the specified basic properties.
    /// </summary>
    IReadOnlyDictionary<string, string> ExtractHeaders(IReadOnlyBasicProperties? properties);
}
