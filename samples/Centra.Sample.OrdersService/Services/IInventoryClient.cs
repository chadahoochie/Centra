using Centra.Invocation;

namespace Centra.Sample.OrdersService.Services;

[ServiceClient("inventory-service")]
public interface IInventoryClient
{
    [ServiceMethod("items/check-stock", "POST")]
    Task<bool> CheckStockAsync(string productId, int quantity);
}
