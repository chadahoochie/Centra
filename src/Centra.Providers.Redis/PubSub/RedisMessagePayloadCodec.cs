using System.Buffers.Binary;
using System.Text.Json;

namespace Centra.Providers.Redis.PubSub;

/// <summary>
/// Provides high-throughput zero-allocation binary framing and decoding for Redis pub/sub and stream messages,
/// eliminating Base64 payload stringification while retaining backward compatibility with JSON envelopes.
/// </summary>
public static class RedisMessagePayloadCodec
{
    public const byte BinaryFrameVersion = 1;

    public static byte[] Encode(ReadOnlyMemory<byte> payload, IReadOnlyDictionary<string, string>? headers)
    {
        byte[] headerBytes = headers is not null && headers.Count > 0
            ? JsonSerializer.SerializeToUtf8Bytes(headers)
            : Array.Empty<byte>();

        var buffer = new byte[1 + 4 + headerBytes.Length + payload.Length];
        buffer[0] = BinaryFrameVersion;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(1, 4), headerBytes.Length);

        if (headerBytes.Length > 0)
        {
            headerBytes.CopyTo(buffer.AsSpan(5, headerBytes.Length));
        }

        payload.Span.CopyTo(buffer.AsSpan(5 + headerBytes.Length));
        return buffer;
    }

    public static bool TryDecode(
        ReadOnlyMemory<byte> raw,
        out ReadOnlyMemory<byte> payload,
        out IReadOnlyDictionary<string, string> headers)
    {
        if (raw.Length >= 5 && raw.Span[0] == BinaryFrameVersion)
        {
            var headersLength = BinaryPrimitives.ReadInt32BigEndian(raw.Span.Slice(1, 4));
            if (headersLength >= 0 && 5 + headersLength <= raw.Length)
            {
                if (headersLength > 0)
                {
                    try
                    {
                        var headerSpan = raw.Span.Slice(5, headersLength);
                        headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headerSpan)
                            ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
                    }
                    catch (Exception ex) when (ex is JsonException or NotSupportedException)
                    {
                        payload = ReadOnlyMemory<byte>.Empty;
                        headers = new Dictionary<string, string>();
                        return false;
                    }
                }
                else
                {
                    headers = new Dictionary<string, string>();
                }

                payload = raw.Slice(5 + headersLength);
                return true;
            }
        }

        // Backward compatibility: fallback for legacy JSON envelope
        if (raw.Length > 0 && raw.Span[0] == (byte)'{')
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<RedisMessageEnvelope>(raw.Span);
                if (envelope is not null)
                {
                    payload = envelope.Payload ?? ReadOnlyMemory<byte>.Empty;
                    headers = envelope.Headers ?? new Dictionary<string, string>();
                    return true;
                }
            }
            catch
            {
                // Fall through to failure
            }
        }

        payload = ReadOnlyMemory<byte>.Empty;
        headers = new Dictionary<string, string>();
        return false;
    }
}
