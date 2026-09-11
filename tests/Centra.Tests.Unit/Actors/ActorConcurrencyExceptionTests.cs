using Centra.Actors;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorConcurrencyExceptionTests
{
    [Fact]
    public void Should_Set_Properties_With_Inner_Exception()
    {
        var identity = new ActorIdentity("OrderActor", "order-123");
        var inner = new InvalidOperationException("Version mismatch");
        var ex = new ActorConcurrencyException(identity, "order_state", "Conflict occurred", inner);

        ex.Identity.ShouldBe(identity);
        ex.StateName.ShouldBe("order_state");
        ex.Message.ShouldBe("Conflict occurred");
        ex.InnerException.ShouldBe(inner);
    }
}
