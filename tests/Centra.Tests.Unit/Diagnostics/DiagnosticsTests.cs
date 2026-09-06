using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Tests.Unit.Common;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Diagnostics;

public sealed class DiagnosticsTests
{
    [Fact]
    public void Should_Create_Producer_Span_When_Publishing_Event()
    {
        // Arrange
        using var listener = new TestActivityListener();

        // Act
        using (var activity = CentraDiagnostics.StartPublishActivity("orders-bus", "orders.created"))
        {
            activity.ShouldNotBeNull();
            activity.Kind.ShouldBe(ActivityKind.Producer);
            activity.GetTagItem("centra.component").ShouldBe("pubsub");
            activity.GetTagItem("messaging.destination").ShouldBe("orders.created");
            activity.GetTagItem("centra.pubsub.name").ShouldBe("orders-bus");
        }

        // Assert
        listener.StoppedActivities.Count.ShouldBe(1);
    }

    [Fact]
    public void Should_Create_Consumer_Span_Linked_To_Producer_Parent()
    {
        // Arrange
        using var listener = new TestActivityListener();

        // 1. Producer activity
        ActivityContext producerContext;
        using (var producer = CentraDiagnostics.StartPublishActivity("orders-bus", "orders.created"))
        {
            producer.ShouldNotBeNull();
            producerContext = producer.Context;
        }

        // 2. Consumer activity linked to parent
        using (var consumer = CentraDiagnostics.StartProcessActivity("orders-bus", "orders.created", producerContext))
        {
            consumer.ShouldNotBeNull();
            consumer.Kind.ShouldBe(ActivityKind.Consumer);
            consumer.ParentSpanId.ToHexString().ShouldBe(producerContext.SpanId.ToHexString());
            consumer.TraceId.ShouldBe(producerContext.TraceId);
            consumer.GetTagItem("centra.component").ShouldBe("pubsub");
            consumer.GetTagItem("messaging.operation").ShouldBe("process");
        }

        // Assert
        listener.StoppedActivities.Count.ShouldBe(2);
    }

    [Fact]
    public void Should_Record_State_Metrics_Accurately()
    {
        // Arrange
        using var meterListener = new TestMeterListener();

        // Act
        CentraMeters.RecordStateOperation("order-store", "set", "success", 4.2);

        // Assert
        var measurements = meterListener.Measurements;
        measurements.ShouldContain(m => m.InstrumentName == "centra.state.operations.total" && (long)m.Value == 1);
        measurements.ShouldContain(m => m.InstrumentName == "centra.state.operation.duration" && Math.Abs((double)m.Value - 4.2) < 0.001);
    }

    [Fact]
    public void Should_Record_PubSub_Publish_Metrics_Accurately()
    {
        // Arrange
        using var meterListener = new TestMeterListener();

        // Act
        CentraMeters.RecordPubSubPublished("event-bus", "orders.created", "success", 2.5);

        // Assert
        var measurements = meterListener.Measurements;
        measurements.ShouldContain(m => m.InstrumentName == "centra.pubsub.messages.published" && (long)m.Value == 1);
        measurements.ShouldContain(m => m.InstrumentName == "centra.pubsub.publish.duration" && Math.Abs((double)m.Value - 2.5) < 0.001);
    }
}
