using Centra.Events;
using Centra.PubSub;

namespace Centra.Tests.Unit.Hosting;

public sealed class DummyTenantEventHandler : IEventHandler<DummyTenantEvent>
{
    public bool Handled { get; set; }
    public int InvocationCount { get; set; }

    public Task<EventHandlingResult> HandleAsync(DummyTenantEvent @event, EventContext context, CancellationToken ct)
    {
        Handled = true;
        InvocationCount++;
        return Task.FromResult(EventHandlingResult.Success);
    }
}
