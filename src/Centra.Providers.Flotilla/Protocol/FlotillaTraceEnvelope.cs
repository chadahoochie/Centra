using System.Buffers.Binary;
using System.Diagnostics;

namespace Centra.Providers.Flotilla.Protocol;

/// <summary>
/// Zero-copy binary envelope wrapping an application payload with a distributed W3C trace context
/// for wire propagation across Flotilla consensus transports (UDP, TCP).
/// Matches the 40-byte Flotilla Rust TraceEnvelope binary layout.
/// </summary>
public static class FlotillaTraceEnvelope
{
    /// <summary>
    /// Total fixed size of the envelope header:
    /// 2 bytes magic ('T','E') + 2 bytes version + 32 bytes trace context + 4 bytes payload length = 40 bytes.
    /// </summary>
    public const int HeaderSize = 40;

    /// <summary>
    /// Magic signature byte 0: 'T' (0x54).
    /// </summary>
    public const byte Magic0 = 0x54;

    /// <summary>
    /// Magic signature byte 1: 'E' (0x45).
    /// </summary>
    public const byte Magic1 = 0x45;

    /// <summary>
    /// Envelope format version: 1.
    /// </summary>
    public const ushort Version = 1;

    /// <summary>
    /// Checks if a buffer begins with a valid Flotilla TraceEnvelope header.
    /// </summary>
    public static bool IsEnveloped(ReadOnlySpan<byte> buffer)
    {
        return buffer.Length >= HeaderSize
            && buffer[0] == Magic0
            && buffer[1] == Magic1
            && BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2, 2)) == Version;
    }

    /// <summary>
    /// Wraps a payload with the specified W3C ActivityContext into a newly allocated byte array.
    /// </summary>
    public static byte[] Wrap(ActivityContext context, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[HeaderSize + payload.Length];
        buffer[0] = Magic0;
        buffer[1] = Magic1;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), Version);

        if (context != default)
        {
            context.TraceId.CopyTo(buffer.AsSpan(4, 16));
            context.SpanId.CopyTo(buffer.AsSpan(20, 8));
            buffer[28] = (byte)context.TraceFlags;
        }

        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(36, 4), (uint)payload.Length);
        payload.CopyTo(buffer.AsSpan(HeaderSize));

        return buffer;
    }

    /// <summary>
    /// Unwraps an enveloped buffer, extracting the W3C trace context and the inner payload memory slice.
    /// If the buffer is not enveloped, returns default context and the original buffer.
    /// </summary>
    public static (ActivityContext Context, ReadOnlyMemory<byte> Payload) Unwrap(ReadOnlyMemory<byte> buffer)
    {
        if (!IsEnveloped(buffer.Span))
        {
            return (default, buffer);
        }

        var payloadLen = (int)BinaryPrimitives.ReadUInt32BigEndian(buffer.Span.Slice(36, 4));
        if (buffer.Length < HeaderSize + payloadLen)
        {
            throw new InvalidDataException(
                $"Flotilla trace envelope payload length mismatch: expected {payloadLen} bytes, buffer has {buffer.Length - HeaderSize} bytes");
        }

        var traceIdSpan = buffer.Span.Slice(4, 16);
        var spanIdSpan = buffer.Span.Slice(20, 8);
        var flags = (ActivityTraceFlags)buffer.Span[28];

        ActivityContext context = default;
        var traceIdHex = Convert.ToHexStringLower(traceIdSpan);
        var spanIdHex = Convert.ToHexStringLower(spanIdSpan);

        if (traceIdHex != "00000000000000000000000000000000" && spanIdHex != "0000000000000000")
        {
            var traceId = ActivityTraceId.CreateFromString(traceIdHex);
            var spanId = ActivitySpanId.CreateFromString(spanIdHex);
            context = new ActivityContext(traceId, spanId, flags, traceState: null, isRemote: true);
        }

        var payload = buffer.Slice(HeaderSize, payloadLen);
        return (context, payload);
    }
}
