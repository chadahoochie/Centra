namespace Centra.Sample.Bindings.Handlers;

public sealed record InboundOrder(string OrderId, decimal Amount, string Customer);
