using System.Text;
using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Protocol;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaWireProtocolTests
{
    [Fact]
    public void EncodeAndDecode_Roundtrip_PreservesAllFields()
    {
        var topic = "orders.created";
        var metadata = new Dictionary<string, string>
        {
            ["ce-id"] = "msg-12345",
            ["ce-source"] = "api.orders",
            ["ce-type"] = "OrderCreatedEvent",
        };
        var payload = Encoding.UTF8.GetBytes("{\"order_id\":\"ord_999\",\"amount\":42.50}");

        var encoded = FlotillaWireProtocol.EncodeMessage(topic, metadata, payload);

        encoded.ShouldNotBeNull();
        encoded.Length.ShouldBeGreaterThan(payload.Length);

        var (decodedTopic, decodedMeta, decodedPayload) = FlotillaWireProtocol.DecodeMessage(encoded);

        decodedTopic.ShouldBe(topic);
        decodedMeta.Count.ShouldBe(3);
        decodedMeta["ce-id"].ShouldBe("msg-12345");
        decodedMeta["ce-source"].ShouldBe("api.orders");
        decodedMeta["ce-type"].ShouldBe("OrderCreatedEvent");
        decodedPayload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void EncodeAndDecode_WithNullMetadata_Succeeds()
    {
        var topic = "telemetry.ping";
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        var encoded = FlotillaWireProtocol.EncodeMessage(topic, null, payload);
        var (decodedTopic, decodedMeta, decodedPayload) = FlotillaWireProtocol.DecodeMessage(encoded);

        decodedTopic.ShouldBe(topic);
        decodedMeta.ShouldBeEmpty();
        decodedPayload.ToArray().ShouldBe(payload);
    }

    [Fact]
    public void Encode_WithInvalidTopic_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            FlotillaWireProtocol.EncodeMessage("", null, ReadOnlySpan<byte>.Empty));

        Should.Throw<ArgumentException>(() =>
            FlotillaWireProtocol.EncodeMessage("   ", null, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Decode_WithCorruptedMagic_ThrowsInvalidDataException()
    {
        var payload = Encoding.UTF8.GetBytes("test");
        var encoded = FlotillaWireProtocol.EncodeMessage("test.topic", null, payload);

        // Corrupt magic bytes
        encoded[0] = 0x00;
        encoded[1] = 0x00;

        Should.Throw<InvalidDataException>(() =>
            FlotillaWireProtocol.DecodeMessage(encoded));
    }

    [Fact]
    public void PacketHeader_ReadWrite_Roundtrip_MaintainsExact40ByteLayout()
    {
        FlotillaPacketHeader.HeaderSize.ShouldBe(40);

        var header = new FlotillaPacketHeader(
            magic: FlotillaPacketHeader.ExpectedMagic,
            version: FlotillaPacketHeader.CurrentVersion,
            msgType: (ushort)FlotillaFrameType.ClientProposal,
            senderId: 42,
            receiverId: 100,
            term: 5,
            checksum: 0xAABBCCDD,
            payloadLen: 256);

        Span<byte> buffer = stackalloc byte[40];
        header.WriteTo(buffer);

        var readBack = FlotillaPacketHeader.ReadFrom(buffer);

        readBack.Magic.ShouldBe(FlotillaPacketHeader.ExpectedMagic);
        readBack.Version.ShouldBe((ushort)1);
        readBack.MsgType.ShouldBe((ushort)FlotillaFrameType.ClientProposal);
        readBack.SenderId.ShouldBe(42UL);
        readBack.ReceiverId.ShouldBe(100UL);
        readBack.Term.ShouldBe(5UL);
        readBack.Checksum.ShouldBe(0xAABBCCDD);
        readBack.PayloadLen.ShouldBe(256U);
    }

    [Fact]
    public void CalculateCrc32_ReturnsDeterministicChecksum()
    {
        var data = Encoding.UTF8.GetBytes("123456789");
        var crc = FlotillaCrc32.Calculate(data);

        // Standard IEEE 802.3 CRC32 of "123456789" is 0xCBF43926
        crc.ShouldBe(0xCBF43926);
    }
}
