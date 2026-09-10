using Centra.Hosting.Extensions;
using Centra.Sample.RabbitSimulation.Contracts.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry: exports Centra's ActivitySource ("Centra") and Meter ("Centra"),
// plus ASP.NET Core, HTTP, and runtime instrumentation over OTLP to Aspire Dashboard.
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "rabbit-api"))
    .WithTracing(tracing => tracing
        .AddSource("Centra")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter("Centra")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .UseOtlpExporter();

builder.Services.AddCentra(options =>
{
    options.AppId = "rabbit-api";
});

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Service = "rabbit-api",
    Status = "Healthy",
    Description = "Centra Service Invocation target API for RabbitMQ simulation"
}));

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

app.MapPost("/orders/process", (OrderProcessRequest request, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.OrderId))
    {
        return Results.BadRequest(new { Error = "OrderId is required." });
    }

    // Apply business logic: volume discount for large orders
    var discount = request.TotalAmount > 100m ? 0.05m : 0m;
    var finalAmount = Math.Round(request.TotalAmount * (1m - discount), 2);
    var authCode = $"AUTH-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    logger.LogInformation(
        "[API] Processed Order {OrderId} for customer '{CustomerName}' ({Quantity}x {Item}) - Total: ${Total:F2}, Discount: {Discount:P0}, Final: ${Final:F2}, AuthCode: {AuthCode}",
        request.OrderId,
        request.CustomerName,
        request.Quantity,
        request.ItemDescription,
        request.TotalAmount,
        discount,
        finalAmount,
        authCode);

    var response = new OrderProcessResponse(
        OrderId: request.OrderId,
        Status: "Approved",
        AuthorizationCode: authCode,
        FinalAmount: finalAmount,
        ProcessedAt: DateTimeOffset.UtcNow,
        Message: $"Order {request.OrderId} processed successfully for {request.CustomerName}.");

    return Results.Ok(response);
});

await app.RunAsync();
