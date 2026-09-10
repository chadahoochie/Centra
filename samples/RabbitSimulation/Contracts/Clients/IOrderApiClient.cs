using Centra.Invocation;
using Centra.Sample.RabbitSimulation.Contracts.Models;

namespace Centra.Sample.RabbitSimulation.Contracts.Clients;

[ServiceClient("rabbit-api")]
public interface IOrderApiClient
{
    [ServiceMethod("orders/process", "POST")]
    Task<OrderProcessResponse> ProcessOrderAsync(OrderProcessRequest request, CancellationToken cancellationToken = default);
}
