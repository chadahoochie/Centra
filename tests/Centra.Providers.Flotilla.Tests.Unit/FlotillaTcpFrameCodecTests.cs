using System.Text;
using Centra.Providers.Flotilla.Protocol;
using Centra.Providers.Flotilla.Tcp.Protocol;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaTcpFrameCodecTests
{
    [Fact]
    public void EncodeProposalFrame_ProducesValid40ByteHeaderAndPayload()
    {
        var payload = Encoding.UTF8.GetBytes("flotilla_tcp_test");
        var frame = FlotillaTcpFrameCodec.EncodeProposalFrame(payload);

        frame.Length.ShouldBe(FlotillaPacketHeader.HeaderSize + payload.Length);

        var header = FlotillaPacketHeader.ReadFrom(frame.AsSpan(0, FlotillaPacketHeader.HeaderSize));
        header.Magic.ShouldBe(FlotillaPacketHeader.ExpectedMagic);
        header.Version.ShouldBe(FlotillaPacketHeader.CurrentVersion);
        header.MsgType.ShouldBe((ushort)FlotillaFrameType.ClientProposal);
        header.PayloadLen.ShouldBe((uint)payload.Length);
        header.Checksum.ShouldBe(FlotillaCrc32.Calculate(payload));
    }

    [Fact]
    public async Task ReadReplyFrameAsync_DecodesValidReplyFrame()
    {
        var reply = new FlotillaClientProposalReply(1, 999, 2, 1);
        var replyPayload = new byte[FlotillaClientProposalReply.ReplySize];
        reply.WriteTo(replyPayload);

        var crc = FlotillaCrc32.Calculate(replyPayload);
        var header = new FlotillaPacketHeader(
            magic: FlotillaPacketHeader.ExpectedMagic,
            version: FlotillaPacketHeader.CurrentVersion,
            msgType: (ushort)FlotillaFrameType.ClientProposalReply,
            senderId: 1,
            receiverId: 0,
            term: 2,
            checksum: crc,
            payloadLen: (uint)replyPayload.Length);

        var fullPacket = new byte[FlotillaPacketHeader.HeaderSize + replyPayload.Length];
        header.WriteTo(fullPacket);
        replyPayload.CopyTo(fullPacket.AsSpan(FlotillaPacketHeader.HeaderSize));

        using var ms = new MemoryStream(fullPacket);
        var (readHeader, readReply) = await FlotillaTcpFrameCodec.ReadReplyFrameAsync(ms, verifyChecksum: true);

        readHeader.Magic.ShouldBe(FlotillaPacketHeader.ExpectedMagic);
        readReply.IsSuccess.ShouldBeTrue();
        readReply.Index.ShouldBe(999UL);
        readReply.Term.ShouldBe(2UL);
        readReply.LeaderId.ShouldBe(1UL);
    }

    [Fact]
    public async Task ReadReplyFrameAsync_InvalidMagic_ThrowsInvalidDataException()
    {
        var fullPacket = new byte[72];
        using var ms = new MemoryStream(fullPacket);

        await Should.ThrowAsync<InvalidDataException>(async () =>
        {
            await FlotillaTcpFrameCodec.ReadReplyFrameAsync(ms, verifyChecksum: false);
        });
    }

    [Fact]
    public async Task ReadReplyFrameAsync_ChecksumMismatch_ThrowsInvalidDataException()
    {
        var reply = new FlotillaClientProposalReply(1, 10, 1, 1);
        var replyPayload = new byte[FlotillaClientProposalReply.ReplySize];
        reply.WriteTo(replyPayload);

        var header = new FlotillaPacketHeader(
            magic: FlotillaPacketHeader.ExpectedMagic,
            version: FlotillaPacketHeader.CurrentVersion,
            msgType: (ushort)FlotillaFrameType.ClientProposalReply,
            senderId: 1,
            receiverId: 0,
            term: 1,
            checksum: 0xDEADBEEF, // Incorrect CRC
            payloadLen: (uint)replyPayload.Length);

        var fullPacket = new byte[FlotillaPacketHeader.HeaderSize + replyPayload.Length];
        header.WriteTo(fullPacket);
        replyPayload.CopyTo(fullPacket.AsSpan(FlotillaPacketHeader.HeaderSize));

        using var ms = new MemoryStream(fullPacket);
        await Should.ThrowAsync<InvalidDataException>(async () =>
        {
            await FlotillaTcpFrameCodec.ReadReplyFrameAsync(ms, verifyChecksum: true);
        });
    }
}
