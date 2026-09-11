using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Tests.Unit.Common;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Diagnostics;

[Collection("CentraDiagnostics")]
public sealed class DiagnosticsTests
{
    [Fact]
    public void Should_Create_Producer_Span_When_Publishing_Event()
    {
        // Arrange
        using var listener = new TestActivityListener();

        // Act
        using (var activity = CentraDiagnostics.StartPublishActivity("orders-bus-pub", "orders.created"))
        {
            activity.ShouldNotBeNull();
            activity.Kind.ShouldBe(ActivityKind.Producer);
            activity.GetTagItem("centra.component").ShouldBe("pubsub");
            activity.GetTagItem("messaging.destination").ShouldBe("orders.created");
            activity.GetTagItem("centra.pubsub.name").ShouldBe("orders-bus-pub");
        }

        // Assert
        var activities = listener.StoppedActivities
            .Where(a => a.GetTagItem("centra.pubsub.name") as string == "orders-bus-pub")
            .ToList();
        activities.Count.ShouldBe(1);
    }

    [Fact]
    public void Should_Create_Consumer_Span_Linked_To_Producer_Parent()
    {
        // Arrange
        using var listener = new TestActivityListener();

        // 1. Producer activity
        ActivityContext producerContext;
        using (var producer = CentraDiagnostics.StartPublishActivity("orders-bus-sub", "orders.created"))
        {
            producer.ShouldNotBeNull();
            producerContext = producer.Context;
        }

        // 2. Consumer activity linked to parent
        using (var consumer = CentraDiagnostics.StartProcessActivity("orders-bus-sub", "orders.created", producerContext))
        {
            consumer.ShouldNotBeNull();
            consumer.Kind.ShouldBe(ActivityKind.Consumer);
            consumer.ParentSpanId.ToHexString().ShouldBe(producerContext.SpanId.ToHexString());
            consumer.TraceId.ShouldBe(producerContext.TraceId);
            consumer.GetTagItem("centra.component").ShouldBe("pubsub");
            consumer.GetTagItem("messaging.operation").ShouldBe("process");
        }

        // Assert
        var activities = listener.StoppedActivities
            .Where(a => a.GetTagItem("centra.pubsub.name") as string == "orders-bus-sub")
            .ToList();
        activities.Count.ShouldBe(2);
    }

    [Fact]
    public void Should_Create_Process_Activity_Without_Parent()
    {
        using var listener = new TestActivityListener();

        using var activity = CentraDiagnostics.StartProcessActivity("my-bus", "my.topic");
        activity.ShouldNotBeNull();
        activity.GetTagItem("messaging.source").ShouldBe("my.topic");
    }

    [Fact]
    public void Should_Create_Invoke_Client_And_Server_Activities()
    {
        using var listener = new TestActivityListener();

        ActivityContext clientContext;
        using (var client = CentraDiagnostics.StartInvokeClientActivity("inventory-service", "get-items"))
        {
            client.ShouldNotBeNull();
            client.Kind.ShouldBe(ActivityKind.Client);
            client.GetTagItem("peer.service").ShouldBe("inventory-service");
            client.GetTagItem("rpc.method").ShouldBe("get-items");
            clientContext = client.Context;
        }

        using (var server = CentraDiagnostics.StartInvokeServerActivity("get-items", clientContext))
        {
            server.ShouldNotBeNull();
            server.Kind.ShouldBe(ActivityKind.Server);
            server.GetTagItem("rpc.method").ShouldBe("get-items");
        }

        using (var serverNoParent = CentraDiagnostics.StartInvokeServerActivity("get-items"))
        {
            serverNoParent.ShouldNotBeNull();
        }
    }

    [Fact]
    public void Should_Create_State_And_Lock_Activities()
    {
        using var listener = new TestActivityListener();

        using (var state = CentraDiagnostics.StartStateActivity("get", "order-store", "order-1"))
        {
            state.ShouldNotBeNull();
            state.GetTagItem("centra.store.name").ShouldBe("order-store");
            state.GetTagItem("centra.key").ShouldBe("order-1");
            state.GetTagItem("centra.operation").ShouldBe("get");
        }

        using (var lockAct = CentraDiagnostics.StartLockActivity("acquire", "redis-lock", "res-42"))
        {
            lockAct.ShouldNotBeNull();
            lockAct.GetTagItem("centra.lock_store.name").ShouldBe("redis-lock");
            lockAct.GetTagItem("centra.resource").ShouldBe("res-42");
        }
    }

    [Fact]
    public void Should_Create_Binding_Activities()
    {
        using var listener = new TestActivityListener();

        using (var output = CentraDiagnostics.StartBindingOutputActivity("cron-trigger", "tick"))
        {
            output.ShouldNotBeNull();
            output.GetTagItem("centra.binding.name").ShouldBe("cron-trigger");
            output.GetTagItem("centra.binding.operation").ShouldBe("tick");
        }

        using (var outputNoOp = CentraDiagnostics.StartBindingOutputActivity("cron-trigger", null))
        {
            outputNoOp.ShouldNotBeNull();
            outputNoOp.GetTagItem("centra.binding.name").ShouldBe("cron-trigger");
        }

        using (var input = CentraDiagnostics.StartBindingInputActivity("webhook-trigger"))
        {
            input.ShouldNotBeNull();
            input.GetTagItem("centra.binding.name").ShouldBe("webhook-trigger");
        }

        using (var inputWithParent = CentraDiagnostics.StartBindingInputActivity("webhook-trigger", ActivityContext.Parse("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", null)))
        {
            inputWithParent.ShouldNotBeNull();
        }
    }

    [Fact]
    public void Should_Create_Actor_Invoke_Activity()
    {
        using var listener = new TestActivityListener();

        using var actor = CentraDiagnostics.StartActorInvokeActivity("OrderActor", "order-123", "Submit");
        actor.ShouldNotBeNull();
        actor.GetTagItem("actor.type").ShouldBe("OrderActor");
        actor.GetTagItem("actor.id").ShouldBe("order-123");
        actor.GetTagItem("actor.method").ShouldBe("Submit");
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
        measurements.ShouldContain(m => m.InstrumentName == "centra.state.operations" && (long)m.Value == 1);
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
