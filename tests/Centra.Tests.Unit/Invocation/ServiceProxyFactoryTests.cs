using Centra.Invocation;
using Centra.Tests.Unit.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Invocation;

public sealed class ServiceProxyFactoryTests
{
    private readonly IServiceInvoker _invoker = Substitute.For<IServiceInvoker>();

    [Theory, AutoNSubstituteData]
    public async Task Should_Create_Typed_Client_Proxy_And_Invoke_Correct_Method(
        string productId,
        int quantity,
        bool isAvailable,
        int inStock)
    {
        // Arrange
        var request = new TestStockRequest(productId, quantity);
        var expectedResponse = new TestStockResponse(isAvailable, inStock);

        _invoker.InvokeMethodAsync<TestStockRequest, TestStockResponse>(
            "inventory-app",
            "check-stock",
            request,
            "POST",
            Arg.Any<ServiceInvocationOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TestStockResponse>(expectedResponse));

        var client = ServiceProxyFactory.Create<ITestInventoryService>(_invoker);

        // Act
        var response = await client.CheckStockAsync(request);

        // Assert
        response.ShouldNotBeNull();
        response.IsAvailable.ShouldBe(isAvailable);
        response.InStockQuantity.ShouldBe(inStock);

        await _invoker.Received(1).InvokeMethodAsync<TestStockRequest, TestStockResponse>(
            "inventory-app",
            "check-stock",
            request,
            "POST",
            Arg.Any<ServiceInvocationOptions?>(),
            Arg.Any<CancellationToken>());
    }
}
