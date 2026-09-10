using System.Text.Json;
using Centra.Events;
using Centra.Invocation;
using Centra.Providers.RabbitMQ.Options;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using Centra.Sample.RabbitSimulation.Contracts.Clients;
using Centra.Sample.RabbitSimulation.Contracts.Models;
using Centra.Sample.RabbitSimulation.Producer.Services;
using Centra.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Shouldly;
using Testcontainers.RabbitMq;
using Xunit;

namespace Centra.Tests.Integration.Providers;

public sealed class RabbitMQServiceInvocationIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitContainer = new RabbitMqBuilder("rabbitmq:4.3-management")
        .Build();

    private TestServer? _apiServer;
    private HttpClient? _apiHttpClient;
    private RabbitMQPubSubDriver? _rabbitDriver;

    public async Task InitializeAsync()
    {
        await _rabbitContainer.StartAsync();

        // 1. Build and start in-memory TestServer simulating rabbit-api
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var app = builder.Build();
        app.MapPost("/orders/process", (OrderProcessRequest request) =>
        {
            var discount = request.TotalAmount > 100m ? 0.05m : 0m;
            var finalAmount = Math.Round(request.TotalAmount * (1m - discount), 2);
            var authCode = $"AUTH-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

            var response = new OrderProcessResponse(
                OrderId: request.OrderId,
                Status: "Approved",
                AuthorizationCode: authCode,
                FinalAmount: finalAmount,
                ProcessedAt: DateTimeOffset.UtcNow,
                Message: $"Order {request.OrderId} authorized.");

            return Results.Ok(response);
        });

        await app.StartAsync();
        _apiServer = app.GetTestServer();
        _apiHttpClient = _apiServer.CreateClient();

        // 2. Build RabbitMQ PubSub Driver
        var factory = new ConnectionFactory
        {
            Uri = new Uri(_rabbitContainer.GetConnectionString())
        };

        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMQProviderOptions
        {
            ExchangeName = "centra.pubsub.sim-it",
            DefaultPubSubName = "pubsub",
            QueuePrefix = "centra-sim-it"
        });

        _rabbitDriver = new RabbitMQPubSubDriver(factory, options);
    }

    public async Task DisposeAsync()
    {
        if (_rabbitDriver is not null)
        {
            await _rabbitDriver.DisposeAsync();
        }

        _apiHttpClient?.Dispose();
        _apiServer?.Dispose();
        await _rabbitContainer.DisposeAsync();
    }

    [Fact]
    public async Task Should_Flow_EndToEnd_From_Bogus_Producer_Through_RabbitMQ_To_Consumer_And_Service_Invocation()
    {
        _rabbitDriver.ShouldNotBeNull();
        _apiHttpClient.ShouldNotBeNull();

        // 1. Setup Centra Service Invoker connected to TestServer
        var invoker = new CentraServiceInvoker(_apiHttpClient);
        var orderApiClient = ServiceProxyFactory.Create<IOrderApiClient>(invoker);

        // 2. Setup TCS to capture the end-to-end result
        var completionTcs = new TaskCompletionSource<OrderProcessResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 3. Subscribe consumer handler to 'orders.new'
        await _rabbitDriver.SubscribeAsync("pubsub", "orders.new", async (payload, headers, ct) =>
        {
            try
            {
                var order = JsonCentraSerializer.Default.Deserialize<OrderMessage>(payload);
                if (order is null)
                {
                    return EventHandlingResult.Drop;
                }

                var total = order.Quantity * order.UnitPrice;
                var request = new OrderProcessRequest(
                    OrderId: order.OrderId,
                    CustomerName: order.CustomerName,
                    ItemDescription: order.ItemDescription,
                    Quantity: order.Quantity,
                    UnitPrice: order.UnitPrice,
                    TotalAmount: total,
                    SubmittedAt: order.CreatedAt);

                // Invoke target API via Centra Service Invocation proxy
                var response = await orderApiClient.ProcessOrderAsync(request, ct);
                completionTcs.TrySetResult(response);
                return EventHandlingResult.Success;
            }
            catch (Exception ex)
            {
                completionTcs.TrySetException(ex);
                return EventHandlingResult.Retry;
            }
        });

        // 4. Generate bogus order via BogusOrderGenerator
        var generator = new BogusOrderGenerator();
        var bogusOrder = generator.Generate();
        bogusOrder.OrderId.ShouldStartWith("ord-");
        bogusOrder.CustomerName.ShouldNotBeNullOrWhiteSpace();

        // 5. Pack CloudEvent and publish to RabbitMQ
        var orderBytes = JsonCentraSerializer.Default.Serialize(bogusOrder);
        var headers = new Dictionary<string, string>
        {
            ["ce-id"] = Guid.NewGuid().ToString("N"),
            ["ce-type"] = "orders.new",
            ["ce-source"] = "centra://rabbit-producer",
            ["ce-specversion"] = "1.0",
            ["ce-correlationid"] = "corr-bogus-001",
            ["traceparent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
        };

        await _rabbitDriver.PublishAsync("pubsub", "orders.new", orderBytes, headers);

        // 6. Await end-to-end receipt and invocation
        var completedTask = await Task.WhenAny(completionTcs.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        completedTask.ShouldBe(completionTcs.Task, "Timed out waiting for consumer to process message and invoke API");

        var apiResponse = await completionTcs.Task;
        apiResponse.ShouldNotBeNull();
        apiResponse.OrderId.ShouldBe(bogusOrder.OrderId);
        apiResponse.Status.ShouldBe("Approved");
        apiResponse.AuthorizationCode.ShouldStartWith("AUTH-");
        apiResponse.FinalAmount.ShouldBeGreaterThan(0m);
    }
}
