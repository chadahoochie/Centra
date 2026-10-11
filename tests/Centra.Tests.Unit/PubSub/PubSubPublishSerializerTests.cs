using System.Text;
using Centra.Drivers;
using Centra.Events;
using Centra.PubSub;
using Centra.PubSub.Outbox;
using Centra.Registry;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub;

public sealed class PubSubPublishSerializerTests
{
    private readonly ComponentRegistry _registry = new();
    private readonly IPubSubDriver _driver = Substitute.For<IPubSubDriver>();

    public PubSubPublishSerializerTests()
    {
        _registry.RegisterPubSubDriver("test-bus", _driver);
    }

    [Fact]
    public async Task Publish_With_Custom_Serializer_In_Client_Uses_Custom_Serializer_And_Sets_ContentType_Header()
    {
        var custom = new CustomTestSerializer();
        var client = new CentraPubSubClient(_registry, "test-service", "test-bus", serializer: custom);
        var evt = new CustomTestEvent("Hello Custom", 42);

        await client.PublishAsync("custom-topic", evt);

        await _driver.Received(1).PublishAsync(
            "test-bus",
            "custom-topic",
            Arg.Is<ReadOnlyMemory<byte>>(m => Encoding.UTF8.GetString(m.ToArray()).StartsWith("CUSTOM:", StringComparison.Ordinal)),
            Arg.Is<IReadOnlyDictionary<string, string>>(headers =>
                headers[CloudEventConstants.DataContentTypeHeader] == "application/x-custom" &&
                headers[CloudEventConstants.TypeHeader] == "custom.event"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_With_ContentType_Option_Resolves_Serializer_From_Registry()
    {
        var custom = new CustomTestSerializer();
        var serializerRegistry = new CentraPubSubSerializerRegistry();
        serializerRegistry.Register("application/x-custom", custom);

        var client = new CentraPubSubClient(_registry, "test-service", "test-bus", serializerRegistry: serializerRegistry);
        var evt = new CustomTestEvent("Via Options", 99);
        var options = new PubSubPublishOptions { ContentType = "application/x-custom" };

        await client.PublishAsync("custom-topic", evt, options);

        await _driver.Received(1).PublishAsync(
            "test-bus",
            "custom-topic",
            Arg.Is<ReadOnlyMemory<byte>>(m => Encoding.UTF8.GetString(m.ToArray()).StartsWith("CUSTOM:", StringComparison.Ordinal)),
            Arg.Is<IReadOnlyDictionary<string, string>>(headers =>
                headers[CloudEventConstants.DataContentTypeHeader] == "application/x-custom"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_In_Structured_Mode_With_Custom_Serializer_Packs_Data_With_Custom_ContentType()
    {
        var custom = new CustomTestSerializer();
        var client = new CentraPubSubClient(_registry, "test-service", "test-bus", serializer: custom);
        var evt = new CustomTestEvent("Structured Custom", 123);
        var options = new PubSubPublishOptions { Mode = CloudEventMode.Structured };

        await client.PublishAsync("custom-topic", evt, options);

        await _driver.Received(1).PublishAsync(
            "test-bus",
            "custom-topic",
            Arg.Is<ReadOnlyMemory<byte>>(m => m.Length > 0),
            Arg.Is<IReadOnlyDictionary<string, string>>(headers =>
                headers[CloudEventConstants.DataContentTypeHeader] == "application/cloudevents+json"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishBatch_With_Custom_Serializer_Applies_To_All_Events()
    {
        var custom = new CustomTestSerializer();
        var client = new CentraPubSubClient(_registry, "test-service", "test-bus", serializer: custom);
        var events = new[]
        {
            new CustomTestEvent("Batch 1", 1),
            new CustomTestEvent("Batch 2", 2)
        };

        await client.PublishBatchAsync("custom-topic", events);

        await _driver.Received(1).PublishBatchAsync(
            "test-bus",
            "custom-topic",
            Arg.Is<IReadOnlyList<PubSubMessage>>(list =>
                list.Count == 2 &&
                Encoding.UTF8.GetString(list[0].Payload.ToArray()).StartsWith("CUSTOM:", StringComparison.Ordinal) &&
                Encoding.UTF8.GetString(list[1].Payload.ToArray()).StartsWith("CUSTOM:", StringComparison.Ordinal) &&
                list[0].Metadata != null &&
                list[1].Metadata != null &&
                list[0].Metadata![CloudEventConstants.DataContentTypeHeader] == "application/x-custom" &&
                list[1].Metadata![CloudEventConstants.DataContentTypeHeader] == "application/x-custom"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutboxPublisher_With_Custom_Serializer_Enqueues_Custom_Payload_And_Header()
    {
        var outboxStore = Substitute.For<IOutboxStore>();
        var custom = new CustomTestSerializer();
        var publisher = new CentraOutboxPublisher(outboxStore, "test-bus", "outbox-service", serializer: custom);
        var evt = new CustomTestEvent("Outbox Event", 555);

        await publisher.EnqueueAsync("outbox-topic", evt);

        await outboxStore.Received(1).EnqueueAsync(
            Arg.Is<OutboxMessage>(msg =>
                msg.Topic == "outbox-topic" &&
                msg.PubSubName == "test-bus" &&
                Encoding.UTF8.GetString(msg.Payload.ToArray()).StartsWith("CUSTOM:", StringComparison.Ordinal) &&
                msg.Headers[CloudEventConstants.DataContentTypeHeader] == "application/x-custom"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CloudEventPacker_And_Unpacker_RoundTrip_With_Custom_Serializer()
    {
        var custom = new CustomTestSerializer();
        var evt = new CustomTestEvent("Packer RoundTrip", 777);

        var packed = CloudEventPacker.Pack(evt, "test-source", CloudEventMode.Binary, serializer: custom);

        packed.Headers[CloudEventConstants.DataContentTypeHeader].ShouldBe("application/x-custom");
        Encoding.UTF8.GetString(packed.Payload.ToArray()).ShouldStartWith("CUSTOM:");

        var unpacked = CloudEventUnpacker.Unpack<CustomTestEvent>(packed.Payload, packed.Headers, serializer: custom);

        unpacked.Data.ShouldNotBeNull();
        unpacked.Data.Message.ShouldBe("Packer RoundTrip");
        unpacked.Data.Value.ShouldBe(777);
        unpacked.Context.Source.ShouldBe("test-source");
    }
}
