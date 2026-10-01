using Centra.Providers.Flotilla.Protocol;

namespace Centra.Providers.Flotilla.Tcp.Protocol;

/// <summary>
/// Binary framing codec for encoding client proposals and decoding server replies over TCP streams.
/// </summary>
public static class FlotillaTcpFrameCodec
{
    /// <summary>
    /// Encodes a client proposal into a framed packet containing the 40-byte header and payload.
    /// </summary>
    public static byte[] EncodeProposalFrame(ReadOnlySpan<byte> payload)
    {
        var checksum = FlotillaCrc32.Calculate(payload);
        var header = new FlotillaPacketHeader(
            magic: FlotillaPacketHeader.ExpectedMagic,
            version: FlotillaPacketHeader.CurrentVersion,
            msgType: (ushort)FlotillaFrameType.ClientProposal,
            senderId: 0,
            receiverId: 1,
            term: 1,
            checksum: checksum,
            payloadLen: (uint)payload.Length);

        var packet = new byte[FlotillaPacketHeader.HeaderSize + payload.Length];
        header.WriteTo(packet);
        payload.CopyTo(packet.AsSpan(FlotillaPacketHeader.HeaderSize));

        return packet;
    }

    /// <summary>
    /// Reads and decodes a proposal reply frame from the stream.
    /// </summary>
    public static async ValueTask<(FlotillaPacketHeader Header, FlotillaClientProposalReply Reply)> ReadReplyFrameAsync(
        Stream stream,
        bool verifyChecksum,
        CancellationToken cancellationToken = default)
    {
        var headerBuffer = new byte[FlotillaPacketHeader.HeaderSize];
        await stream.ReadExactlyAsync(headerBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);

        var header = FlotillaPacketHeader.ReadFrom(headerBuffer);
        if (header.Magic != FlotillaPacketHeader.ExpectedMagic)
        {
            throw new InvalidDataException($"Invalid Flotilla magic bytes: 0x{header.Magic:X8}, expected 0x{FlotillaPacketHeader.ExpectedMagic:X8}");
        }

        if (header.MsgType != (ushort)FlotillaFrameType.ClientProposalReply)
        {
            throw new InvalidDataException($"Unexpected message type received: {header.MsgType}, expected ClientProposalReply");
        }

        var payloadBuffer = new byte[header.PayloadLen];
        await stream.ReadExactlyAsync(payloadBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);

        if (verifyChecksum)
        {
            var calculatedCrc = FlotillaCrc32.Calculate(payloadBuffer);
            if (calculatedCrc != header.Checksum)
            {
                throw new InvalidDataException($"Checksum mismatch: expected 0x{header.Checksum:X8}, computed 0x{calculatedCrc:X8}");
            }
        }

        var reply = FlotillaClientProposalReply.ReadFrom(payloadBuffer);
        return (header, reply);
    }
}
