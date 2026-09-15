using Centra.Events;
using Centra.PubSub;

namespace Centra.Tests.Unit.Hosting;

[Topic("pubsub", "offload-topic", EnableTenantOffload = true)]
public sealed class TestOffloadHandler : IEventHandler<DummyTenantEvent>
{
    public Task<EventHandlingResult> HandleAsync(DummyTenantEvent @event, EventContext context, CancellationToken ct)
    {
        return Task.FromResult(EventHandlingResult.Success);
    }
}
