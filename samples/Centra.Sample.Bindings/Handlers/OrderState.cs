namespace Centra.Sample.Bindings.Handlers;

public sealed record OrderState(string OrderId, decimal Amount, string Status);
