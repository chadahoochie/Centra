using Centra.Sample.TenantOffload.Domain;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.Tenancy;

public sealed class TenantConsumerNodeStateTests
{
    [Fact]
    public void Should_Initialize_With_Correct_InstanceId_And_BrokerType()
    {
        var state = new TenantConsumerNodeState("node-42", "RabbitMQ");

        state.InstanceId.ShouldBe("node-42");
        state.BrokerType.ShouldBe("RabbitMQ");
        state.TotalHandled.ShouldBe(0);
        state.TenantHandledCounts.ShouldBeEmpty();
        state.RecentProcessedOrders.ShouldBeEmpty();
    }

    [Fact]
    public void Should_Record_Handled_Orders_And_Track_Tenant_Counts()
    {
        var state = new TenantConsumerNodeState("node-1", "RabbitMQ");

        state.RecordHandled("tenant-alpha", "ord-1", 100m, false);
        state.RecordHandled("tenant-alpha", "ord-2", 200m, false);
        state.RecordHandled("tenant-mega", "ord-3", 50m, true);

        state.TotalHandled.ShouldBe(3);
        state.TenantHandledCounts["tenant-alpha"].ShouldBe(2);
        state.TenantHandledCounts["tenant-mega"].ShouldBe(1);
        state.RecentProcessedOrders.Count.ShouldBe(3);

        var lastOrder = state.RecentProcessedOrders[^1];
        lastOrder.OrderId.ShouldBe("ord-3");
        lastOrder.TenantId.ShouldBe("tenant-mega");
        lastOrder.Amount.ShouldBe(50m);
        lastOrder.WasOffloaded.ShouldBeTrue();
        lastOrder.InstanceId.ShouldBe("node-1");
    }

    [Fact]
    public void Should_Throw_On_Invalid_Arguments()
    {
        var state = new TenantConsumerNodeState("node-1", "RabbitMQ");

        Should.Throw<ArgumentException>(() => state.RecordHandled(string.Empty, "ord-1", 10m, false));
        Should.Throw<ArgumentException>(() => state.RecordHandled("tenant-alpha", string.Empty, 10m, false));
    }
}
