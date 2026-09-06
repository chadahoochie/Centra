using Centra.Hosting.Extensions;
using Centra.Locks;
using Centra.Providers.InMemory.Extensions;
using Centra.PubSub;
using Centra.Sample.OrdersService.Domain;
using Centra.Sample.OrdersService.Handlers;
using Centra.Sample.OrdersService.Services;
using Centra.State;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Centra Distributed Framework
builder.Services.AddCentra(options =>
{
    options.AppId = "orders-service";
    options.DefaultStateStore = "orders-statestore";
    options.DefaultPubSub = "orders-pubsub";
    options.DefaultLockStore = "orders-lockstore";
});

// 2. Add In-Memory Provider for zero-dependency local dev & testing
builder.Services.AddCentraInMemory(
    defaultStateStore: "orders-statestore",
    defaultPubSub: "orders-pubsub",
    defaultLockStore: "orders-lockstore");

// 3. Register typed service client proxy & event handlers
builder.Services.AddCentraServiceClient<IInventoryClient>();
builder.Services.AddCentraEventHandler<PaymentNotificationHandler, OrderCreatedEvent>(
    pubSubName: "orders-pubsub",
    topic: "orders.created");

var app = builder.Build();

// 4. Pure domain endpoint - "Code focused on code"
app.MapPost("/orders", async (
    CreateOrderRequest request,
    IStateStore<Order> stateStore,
    IPubSubClient pubSub,
    IDistributedLockProvider lockProvider) =>
{
    var orderId = $"ord-{Guid.NewGuid():N}";
    var total = request.Quantity * request.UnitPrice;

    // Mutex lock on the product to prevent concurrent race conditions
    await using var productLock = await lockProvider.AcquireLockAsync(
        "orders-lockstore",
        request.ProductId,
        expiryTime: TimeSpan.FromSeconds(30),
        timeout: TimeSpan.FromSeconds(5));

    // Save initial state with automatic ETag concurrency
    var order = new Order(orderId, request.CustomerId, request.ProductId, request.Quantity, total, "Created");
    await stateStore.SetAsync(orderId, order);

    // Publish CloudEvent v1.0 in Binary mode with W3C tracecontext and ambient correlation
    var evt = new OrderCreatedEvent(orderId, request.CustomerId, request.ProductId, total);
    await pubSub.PublishAsync("orders.created", evt);

    return Results.Created($"/orders/{orderId}", order);
});

app.MapGet("/orders/{id}", async (string id, IStateStore<Order> stateStore) =>
{
    var entry = await stateStore.GetAsync(id);
    return entry.HasValue ? Results.Ok(entry.Value.Value) : Results.NotFound();
});

// 5. Mount Centra CloudEvents & Bindings route dispatcher
app.MapCentraEndpoints();

app.Run();

// Required for WebApplicationFactory in testing
public partial class Program { }
