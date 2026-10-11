using Centra.Events;

namespace Centra.Tests.Unit.PubSub;

[EventContract("custom.event", Version = "1")]
public sealed record CustomTestEvent(string Message, int Value);
