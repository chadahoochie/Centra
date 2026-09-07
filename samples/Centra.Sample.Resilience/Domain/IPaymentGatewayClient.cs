using Centra.Invocation;

namespace Centra.Sample.Resilience.Domain;

[ServiceClient("payment-gateway")]
public interface IPaymentGatewayClient
{
    [ServiceMethod("api/v1/payments/process", "POST")]
    Task<PaymentResponse> ProcessPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default);
}
