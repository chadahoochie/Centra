using Centra.Invocation;

namespace Centra.Tests.Unit.Invocation;

[ServiceClient("inventory-app")]
public interface ITestInventoryService
{
    [ServiceMethod("check-stock", "POST")]
    Task<TestStockResponse> CheckStockAsync(TestStockRequest request);
}
