using Shouldly;
using Xunit;

namespace Centra.Generators.Tests.Unit;

public sealed class ServiceClientGeneratorTests
{
    [Fact]
    public void Should_Generate_Proxy_For_ServiceClient_Interface()
    {
        // Arrange
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Centra.Invocation;

            namespace SampleApp.Services;

            public sealed record CreateOrderRequest(string Item, int Quantity);
            public sealed record OrderResponse(string OrderId, string Status);

            [ServiceClient("orders-service")]
            public interface IOrdersService
            {
                Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
                ValueTask<OrderResponse> GetOrderAsync(string orderId, CancellationToken cancellationToken = default);
                Task CancelOrderAsync(string orderId, CancellationToken cancellationToken = default);
            }
            """;

        // Act
        var (diagnostics, generated) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.ShouldBeEmpty();
        generated.Length.ShouldBe(1);

        var proxySource = generated[0].SourceText;
        proxySource.ShouldContain("public sealed class OrdersServiceProxy : IOrdersService");
        proxySource.ShouldContain("private readonly global::Centra.Invocation.IServiceInvoker _invoker;");
        proxySource.ShouldContain("public OrdersServiceProxy(global::Centra.Invocation.IServiceInvoker invoker, string? appId = null)");
        proxySource.ShouldContain("_appId = appId ?? \"orders-service\";");
        proxySource.ShouldContain("public static IOrdersService Create(global::Centra.Invocation.IServiceInvoker invoker, string? appId = null)");
        proxySource.ShouldContain("await _invoker.InvokeMethodAsync<");
        proxySource.ShouldContain("CreateOrderAsync");
        proxySource.ShouldContain("GetOrderAsync");
        proxySource.ShouldContain("CancelOrderAsync");
        proxySource.ShouldContain("public static class OrdersServiceProxyExtensions");
        proxySource.ShouldContain("AddOrdersServiceClient");
    }

    [Fact]
    public void Should_Support_ServiceMethod_Attribute_And_Custom_Verbs()
    {
        // Arrange
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Centra.Invocation;

            namespace SampleApp.Services;

            [ServiceClient("payments-service")]
            public interface IPaymentsService
            {
                [ServiceMethod("charge-card", "PUT")]
                Task<string> ProcessPaymentAsync(string cardToken, CancellationToken cancellationToken = default);
            }
            """;

        // Act
        var (diagnostics, generated) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.ShouldBeEmpty();
        generated.Length.ShouldBe(1);

        var proxySource = generated[0].SourceText;
        proxySource.ShouldContain("\"charge-card\"");
        proxySource.ShouldContain("\"PUT\"");
    }

    [Fact]
    public void Should_Not_Generate_When_Interface_Lacks_ServiceClient_Attribute()
    {
        // Arrange
        var source = """
            namespace SampleApp.Services;

            public interface IPlainService
            {
                void DoSomething();
            }
            """;

        // Act
        var (diagnostics, generated) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.ShouldBeEmpty();
        generated.ShouldBeEmpty();
    }
}
