using Centra.Events;
using Centra.Hosting.Extensions;
using Centra.Invocation;
using Centra.Locks;
using Centra.Providers.InMemory.Extensions;
using Centra.PubSub;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.Workflows;

public sealed class EndToEndOrdersWorkflowTests
{
    [Fact]
    public async Task Should_Execute_Complete_Distributed_Workflow_With_CloudEvents_State_Locks_And_PubSub()
    {
        // Arrange
        IntegrationPaymentHandler.WasExecuted = false;
        IntegrationPaymentHandler.LastReceivedCorrelationId = null;

        var mockInvoker = Substitute.For<IServiceInvoker>();
        mockInvoker.InvokeMethodAsync<object, bool>(
            "inventory-service",
            "check-availability",
            Arg.Any<object>(),
            Arg.Any<string?>(),
            Arg.Any<ServiceInvocationOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddCentra(options =>
                {
                    options.AppId = "orders-service";
                    options.DefaultStateStore = "orders-statestore";
                    options.DefaultPubSub = "orders-pubsub";
                    options.DefaultLockStore = "orders-lockstore";
                });
                services.AddCentraInMemory(
                    defaultStateStore: "orders-statestore",
                    defaultPubSub: "orders-pubsub",
                    defaultLockStore: "orders-lockstore");

                // Override invoker with mock for inventory check
                services.AddSingleton(mockInvoker);
                services.AddCentraServiceClient<IIntegrationInventoryClient>();

                // Register event handler with pubsub
                services.AddCentraEventHandler<IntegrationPaymentHandler, IntegrationOrderCreatedEvent>(
                    pubSubName: "orders-pubsub",
                    topic: "orders.created");
            })
            .Build();

        await host.StartAsync();

        var stateStore = host.Services.GetRequiredService<IStateStore<IntegrationOrderState>>();
        var pubSub = host.Services.GetRequiredService<IPubSubClient>();
        var lockProvider = host.Services.GetRequiredService<IDistributedLockProvider>();
        var inventoryClient = host.Services.GetRequiredService<IIntegrationInventoryClient>();

        var orderId = "ord-999";
        var productId = "prod-42";
        var correlationId = "corr-unique-999";

        CentraAmbientContext.CorrelationId = correlationId;

        try
        {
            // Act 1: Acquire distributed lock on product
            await using var productLock = await lockProvider.AcquireLockAsync(
                "orders-lockstore",
                productId,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5));

            productLock.ShouldNotBeNull();

            // Act 2: Resilient RPC to Inventory Service via typed client proxy
            var isAvailable = await inventoryClient.CheckAvailabilityAsync(productId, 2);
            isAvailable.ShouldBeTrue();

            // Act 3: Persist initial order state with ETag
            var initialOrder = new IntegrationOrderState(orderId, productId, 2, "Created");
            await stateStore.SetAsync(orderId, initialOrder);

            // Act 4: Publish CloudEvent to PubSub
            var evt = new IntegrationOrderCreatedEvent(orderId, productId, 2);
            await pubSub.PublishAsync("orders.created", evt);

            // Assert: Verify subscriber processed event and updated state to "Paid"
            IntegrationPaymentHandler.WasExecuted.ShouldBeTrue();
            IntegrationPaymentHandler.LastReceivedCorrelationId.ShouldBe(correlationId);

            var finalState = await stateStore.GetAsync(orderId);
            finalState.ShouldNotBeNull();
            finalState.Value.Value.Status.ShouldBe("Paid");
            finalState.Value.Value.OrderId.ShouldBe(orderId);
            finalState.Value.ETag.ShouldNotBeNullOrWhiteSpace();
        }
        finally
        {
            CentraAmbientContext.Clear();
            await host.StopAsync();
        }
    }
}
