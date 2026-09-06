using Centra.Events;

namespace Centra.PubSub;

public interface IEventHandler<in TEvent>
{
    Task<EventHandlingResult> HandleAsync(TEvent @event, EventContext context, CancellationToken cancellationToken = default);
}
