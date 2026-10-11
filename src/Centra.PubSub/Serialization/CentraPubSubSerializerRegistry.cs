using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Centra.Serialization;

namespace Centra.PubSub;

public sealed class CentraPubSubSerializerRegistry : IPubSubSerializerRegistry
{
    private readonly ConcurrentDictionary<string, ICentraSerializer> _serializers = new(StringComparer.OrdinalIgnoreCase);

    public CentraPubSubSerializerRegistry()
    {
        _serializers["application/json"] = JsonCentraSerializer.Default;
    }

    public void Register(string contentType, ICentraSerializer serializer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(serializer);

        _serializers[contentType] = serializer;
    }

    public ICentraSerializer? GetSerializer(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        if (_serializers.TryGetValue(contentType, out var serializer))
        {
            return serializer;
        }

        var semicolonIndex = contentType.IndexOf(';');
        if (semicolonIndex > 0 && _serializers.TryGetValue(contentType[..semicolonIndex].Trim(), out var baseSerializer))
        {
            return baseSerializer;
        }

        return null;
    }

    public bool TryGetSerializer(string contentType, [NotNullWhen(true)] out ICentraSerializer? serializer)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            serializer = null;
            return false;
        }

        if (_serializers.TryGetValue(contentType, out serializer))
        {
            return true;
        }

        var semicolonIndex = contentType.IndexOf(';');
        if (semicolonIndex > 0 && _serializers.TryGetValue(contentType[..semicolonIndex].Trim(), out serializer))
        {
            return true;
        }

        serializer = null;
        return false;
    }
}
