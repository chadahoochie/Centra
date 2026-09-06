using System.Buffers;
using System.Text.Json;
using Centra.Memory;

namespace Centra.Serialization;

public sealed class JsonCentraSerializer : ICentraSerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly JsonSerializerOptions _options;

    public static JsonCentraSerializer Default { get; } = new(DefaultOptions);

    public JsonCentraSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? DefaultOptions;
    }

    public void Serialize<T>(T value, IBufferWriter<byte> writer)
    {
        using var utf8Writer = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(utf8Writer, value, _options);
    }

    public byte[] Serialize<T>(T value)
    {
        using var writer = new PooledByteBufferWriter();
        Serialize(value, writer);
        return writer.ToArray();
    }

    public T? Deserialize<T>(ReadOnlyMemory<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(buffer.Span, _options);
    }

    public object? Deserialize(ReadOnlyMemory<byte> buffer, Type returnType)
    {
        if (buffer.IsEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize(buffer.Span, returnType, _options);
    }
}
