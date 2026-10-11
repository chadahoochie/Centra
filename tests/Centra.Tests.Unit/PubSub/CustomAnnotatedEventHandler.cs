using Centra.Events;
using Centra.PubSub;

namespace Centra.Tests.Unit.PubSub;

[Topic("test-bus", "annotated-topic", SerializerType = typeof(CustomTestSerializer), ContentType = "application/x-custom")]
public sealed class CustomAnnotatedEventHandler : IEventHandler<CustomTestEvent>
{
    public Task<EventHandlingResult> HandleAsync(CustomTestEvent @event, EventContext context, CancellationToken ct)
    {
        return Task.FromResult(EventHandlingResult.Success);
    }
}
