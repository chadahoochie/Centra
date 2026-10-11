using Centra.Events;
using Centra.PubSub;

namespace Centra.Tests.Unit.PubSub;

public sealed class CustomTestEventHandler : IEventHandler<CustomTestEvent>
{
    public CustomTestEvent? ReceivedEvent { get; set; }

    public EventContext? ReceivedContext { get; set; }

    public Task<EventHandlingResult> HandleAsync(CustomTestEvent @event, EventContext context, CancellationToken ct)
    {
        ReceivedEvent = @event;
        ReceivedContext = context;
        return Task.FromResult(EventHandlingResult.Success);
    }
}
