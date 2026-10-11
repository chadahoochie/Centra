using System.Buffers;
using System.Text;
using Centra.PubSub;

namespace Centra.Tests.Unit.PubSub;

public sealed class CustomTestSerializer : IPubSubSerializer
{
    public const string CustomContentType = "application/x-custom";

    public string ContentType => CustomContentType;

    public void Serialize<T>(T value, IBufferWriter<byte> writer)
    {
        var bytes = Serialize(value);
        writer.Write(bytes);
    }

    public byte[] Serialize<T>(T value)
    {
        var text = $"CUSTOM:{System.Text.Json.JsonSerializer.Serialize(value)}";
        return Encoding.UTF8.GetBytes(text);
    }

    public byte[] Serialize(object? value, Type inputType)
    {
        var text = $"CUSTOM:{System.Text.Json.JsonSerializer.Serialize(value, inputType)}";
        return Encoding.UTF8.GetBytes(text);
    }

    public T? Deserialize<T>(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return default;
        }

        var text = Encoding.UTF8.GetString(bytes.Span);
        if (text.StartsWith("CUSTOM:", StringComparison.Ordinal))
        {
            text = text["CUSTOM:".Length..];
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(text);
    }

    public object? Deserialize(ReadOnlyMemory<byte> bytes, Type targetType)
    {
        if (bytes.IsEmpty)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(bytes.Span);
        if (text.StartsWith("CUSTOM:", StringComparison.Ordinal))
        {
            text = text["CUSTOM:".Length..];
        }

        return System.Text.Json.JsonSerializer.Deserialize(text, targetType);
    }
}
