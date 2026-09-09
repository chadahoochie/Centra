using Azure.Messaging.ServiceBus;
using Centra.Events;

namespace Centra.Providers.AzureServiceBus.PubSub;

/// <summary>
/// Default implementation of <see cref="IServiceBusHeaderExtractor"/>.
/// </summary>
public sealed class ServiceBusHeaderExtractor : IServiceBusHeaderExtractor
{
    /// <summary>
    /// Singleton default instance of <see cref="ServiceBusHeaderExtractor"/>.
    /// </summary>
    public static readonly ServiceBusHeaderExtractor Instance = new();

    public IReadOnlyDictionary<string, string> ExtractHeaders(ServiceBusReceivedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (k, v) in message.ApplicationProperties)
        {
            if (v is not null)
            {
                headers[k] = v.ToString()!;
            }
        }

        CopyHeaderIfPresent(headers, CloudEventConstants.IdHeader, message.MessageId);
        CopyHeaderIfPresent(headers, CloudEventConstants.SubjectHeader, message.Subject);
        CopyHeaderIfPresent(headers, CloudEventConstants.CorrelationIdHeader, message.CorrelationId);
        CopyHeaderIfPresent(headers, CloudEventConstants.DataContentTypeHeader, message.ContentType);

        return headers;
    }

    /// <summary>
    /// Copies a header value into the dictionary if present and not already defined.
    /// </summary>
    public static void CopyHeaderIfPresent(Dictionary<string, string> headers, string headerName, string? value)
    {
        if (!string.IsNullOrEmpty(value) && !headers.ContainsKey(headerName))
        {
            headers[headerName] = value;
        }
    }
}
