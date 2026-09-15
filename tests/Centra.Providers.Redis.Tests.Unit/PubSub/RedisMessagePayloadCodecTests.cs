using System.Text;
using System.Text.Json;
using Centra.Providers.Redis.PubSub;
using Shouldly;
using Xunit;

namespace Centra.Providers.Redis.Tests.Unit.PubSub;

public sealed class RedisMessagePayloadCodecTests
{
    [Fact]
    public void Encode_And_TryDecode_BinaryFramedPayload_RoundTripsAccurately()
    {
        // Arrange
        var originalPayload = Encoding.UTF8.GetBytes("raw-binary-payload-data-12345");
        var headers = new Dictionary<string, string>
        {
            ["ce-id"] = "event-42",
            ["ce-type"] = "orders.created",
            ["ce-source"] = "checkout-service"
        };

        // Act
        var encoded = RedisMessagePayloadCodec.Encode(originalPayload, headers);
        var success = RedisMessagePayloadCodec.TryDecode(encoded, out var decodedPayload, out var decodedHeaders);

        // Assert
        success.ShouldBeTrue();
        encoded[0].ShouldBe(RedisMessagePayloadCodec.BinaryFrameVersion);
        decodedPayload.ToArray().ShouldBe(originalPayload);
        decodedHeaders.ShouldNotBeNull();
        decodedHeaders["ce-id"].ShouldBe("event-42");
        decodedHeaders["ce-type"].ShouldBe("orders.created");
        decodedHeaders["ce-source"].ShouldBe("checkout-service");
    }

    [Fact]
    public void Encode_WithNullOrEmptyHeaders_DecodesEmptyHeaders()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("payload-only");

        // Act
        var encoded = RedisMessagePayloadCodec.Encode(payload, null);
        var success = RedisMessagePayloadCodec.TryDecode(encoded, out var decodedPayload, out var decodedHeaders);

        // Assert
        success.ShouldBeTrue();
        decodedPayload.ToArray().ShouldBe(payload);
        decodedHeaders.ShouldNotBeNull();
        decodedHeaders.Count.ShouldBe(0);
    }

    [Fact]
    public void TryDecode_LegacyJsonEnvelope_FallsBackSuccessfully()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("legacy-data");
        var headers = new Dictionary<string, string> { ["legacy-key"] = "legacy-val" };
        var envelope = new RedisMessageEnvelope(headers, payload);
        var legacyBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);

        // Act
        var success = RedisMessagePayloadCodec.TryDecode(legacyBytes, out var decodedPayload, out var decodedHeaders);

        // Assert
        success.ShouldBeTrue();
        decodedPayload.ToArray().ShouldBe(payload);
        decodedHeaders["legacy-key"].ShouldBe("legacy-val");
    }

    [Fact]
    public void TryDecode_InvalidData_ReturnsFalse()
    {
        // Arrange
        var corrupt = new byte[] { 99, 100, 101 };

        // Act
        var success = RedisMessagePayloadCodec.TryDecode(corrupt, out var payload, out var headers);

        // Assert
        success.ShouldBeFalse();
        payload.Length.ShouldBe(0);
        headers.Count.ShouldBe(0);
    }

    [Fact]
    public void TryDecode_MalformedJsonHeaders_ReturnsFalse()
    {
        // Arrange: binary frame version 1 with 4-byte invalid header payload
        var corruptHeader = new byte[] { RedisMessagePayloadCodec.BinaryFrameVersion, 0, 0, 0, 4, 1, 2, 3, 4, 10, 20 };

        // Act
        var success = RedisMessagePayloadCodec.TryDecode(corruptHeader, out var payload, out var headers);

        // Assert
        success.ShouldBeFalse();
        payload.Length.ShouldBe(0);
        headers.Count.ShouldBe(0);
    }
}
