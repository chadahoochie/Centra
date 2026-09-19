using Centra.Events;
using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// Default <see cref="IRabbitMQMessageIdentityReader"/>: prefers the CloudEvents <c>ce-id</c> binary-mode
/// header, falling back to the AMQP <c>message-id</c> property. The delivery tag is deliberately not used -
/// it is reassigned on every redelivery, so counting against it could never terminate.
/// </summary>
public sealed class RabbitMQMessageIdentityReader : IRabbitMQMessageIdentityReader
{
    /// <summary>
    /// Singleton default instance of <see cref="RabbitMQMessageIdentityReader"/>.
    /// </summary>
    public static readonly RabbitMQMessageIdentityReader Instance = new();

    /// <inheritdoc />
    public string? ResolveIdentity(IReadOnlyDictionary<string, string>? headers, IReadOnlyBasicProperties? properties)
    {
        if (headers is not null &&
            headers.TryGetValue(CloudEventConstants.IdHeader, out var cloudEventId) &&
            !string.IsNullOrWhiteSpace(cloudEventId))
        {
            return cloudEventId;
        }

        var messageId = properties?.MessageId;
        return string.IsNullOrWhiteSpace(messageId) ? null : messageId;
    }
}
