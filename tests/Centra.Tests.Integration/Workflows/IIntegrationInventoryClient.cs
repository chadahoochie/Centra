using Centra.Invocation;

namespace Centra.Tests.Integration.Workflows;

[ServiceClient("inventory-service")]
public interface IIntegrationInventoryClient
{
    [ServiceMethod("check-availability", "POST")]
    Task<bool> CheckAvailabilityAsync(string productId, int quantity);
}
