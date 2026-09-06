using System.Diagnostics;
using Centra.Events;
using Centra.Tests.Unit.Common;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Events;

public sealed class CloudEventPackerTests
{
    [Theory, AutoNSubstituteData]
    public void Should_Pack_In_Binary_Mode_With_CloudEvent_And_Trace_Headers(
        string orderId,
        string productId,
        int quantity,
        string source)
    {
        // Arrange
        var evt = new TestOrderCreatedEvent(orderId, productId, quantity);
        using var activity = new Activity("TestPublish").Start();

        CentraAmbientContext.CorrelationId = "corr-123";
        CentraAmbientContext.CausationId = "cause-456";
        CentraAmbientContext.TenantId = "tenant-789";

        try
        {
            // Act
            var packed = CloudEventPacker.Pack(evt, source, CloudEventMode.Binary);

            // Assert
            packed.Mode.ShouldBe(CloudEventMode.Binary);
            packed.Payload.Length.ShouldBeGreaterThan(0);

            var headers = packed.Headers;
            headers[CloudEventConstants.SpecVersionHeader].ShouldBe(CloudEventConstants.SpecVersion10);
            headers[CloudEventConstants.SourceHeader].ShouldBe(source);
            headers[CloudEventConstants.TypeHeader].ShouldBe("orders.created");
            headers[CloudEventConstants.SchemaVersionHeader].ShouldBe("1");
            headers[CloudEventConstants.CorrelationIdHeader].ShouldBe("corr-123");
            headers[CloudEventConstants.CausationIdHeader].ShouldBe("cause-456");
            headers[CloudEventConstants.TenantIdHeader].ShouldBe("tenant-789");
            headers.ContainsKey(CloudEventConstants.IdHeader).ShouldBeTrue();
            headers.ContainsKey(CloudEventConstants.TimeHeader).ShouldBeTrue();
            headers.ContainsKey(CloudEventConstants.TraceParentHeader).ShouldBeTrue();
        }
        finally
        {
            CentraAmbientContext.Clear();
        }
    }

    [Theory, AutoNSubstituteData]
    public void Should_Unpack_Binary_Mode_And_Restore_Ambient_And_Trace_Context(
        string orderId,
        string productId,
        int quantity,
        string source)
    {
        // Arrange
        var evt = new TestOrderCreatedEvent(orderId, productId, quantity);
        CentraAmbientContext.CorrelationId = "corr-abc";
        CentraAmbientContext.CausationId = "cause-def";

        PackedCloudEvent packed;
        try
        {
            packed = CloudEventPacker.Pack(evt, source, CloudEventMode.Binary);
        }
        finally
        {
            CentraAmbientContext.Clear();
        }

        // Act
        var unpacked = CloudEventUnpacker.Unpack<TestOrderCreatedEvent>(packed.Payload, packed.Headers);

        // Assert
        unpacked.Data.ShouldNotBeNull();
        unpacked.Data.OrderId.ShouldBe(orderId);
        unpacked.Data.ProductId.ShouldBe(productId);
        unpacked.Data.Quantity.ShouldBe(quantity);

        unpacked.Context.Source.ShouldBe(source);
        unpacked.Context.Type.ShouldBe("orders.created");
        unpacked.Context.CorrelationId.ShouldBe("corr-abc");
        unpacked.Context.CausationId.ShouldBe("cause-def");
    }

    [Theory, AutoNSubstituteData]
    public void Should_Pack_And_Unpack_In_Structured_Mode(
        string orderId,
        string productId,
        int quantity,
        string source)
    {
        // Arrange
        var evt = new TestOrderCreatedEvent(orderId, productId, quantity);

        // Act
        var packed = CloudEventPacker.Pack(evt, source, CloudEventMode.Structured);
        var unpacked = CloudEventUnpacker.Unpack<TestOrderCreatedEvent>(packed.Payload, packed.Headers);

        // Assert
        packed.Mode.ShouldBe(CloudEventMode.Structured);
        packed.Headers[CloudEventConstants.DataContentTypeHeader].ShouldBe("application/cloudevents+json");

        unpacked.Data.ShouldNotBeNull();
        unpacked.Data.OrderId.ShouldBe(orderId);
        unpacked.Data.ProductId.ShouldBe(productId);
        unpacked.Data.Quantity.ShouldBe(quantity);
        unpacked.Context.Source.ShouldBe(source);
        unpacked.Context.Type.ShouldBe("orders.created");
    }
}
