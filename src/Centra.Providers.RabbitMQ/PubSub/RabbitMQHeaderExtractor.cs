using System.Text;
using RabbitMQ.Client;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// Default implementation of <see cref="IRabbitMQHeaderExtractor"/>.
/// </summary>
public sealed class RabbitMQHeaderExtractor : IRabbitMQHeaderExtractor
{
    /// <summary>
    /// Singleton default instance of <see cref="RabbitMQHeaderExtractor"/>.
    /// </summary>
    public static readonly RabbitMQHeaderExtractor Instance = new();

    public IReadOnlyDictionary<string, string> ExtractHeaders(IReadOnlyBasicProperties? properties)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (properties?.Headers is null)
        {
            return headers;
        }

        foreach (var (k, v) in properties.Headers)
        {
            if (v is byte[] bytes)
            {
                headers[k] = Encoding.UTF8.GetString(bytes);
            }
            else if (v is not null)
            {
                headers[k] = v.ToString()!;
            }
        }

        return headers;
    }
}
