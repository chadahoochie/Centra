namespace Centra.Tests.Integration.Workflows;

public sealed record IntegrationOrderState(
    string OrderId,
    string ProductId,
    int Quantity,
    string Status);
