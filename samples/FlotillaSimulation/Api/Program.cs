using Centra.Sample.FlotillaSimulation.Contracts.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry Setup
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "flotilla-api"))
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

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Service = "flotilla-api",
    Status = "Running",
    Endpoint = "POST /telemetry/process"
}));

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

app.MapPost("/telemetry/process", (TelemetryProcessRequest request, ILogger<Program> logger) =>
{
    logger.LogInformation(
        "[API] Processing telemetry for device {DeviceId}, Status: {Status}, AnomalyScore: {AnomalyScore:F3}",
        request.DeviceId,
        request.Status,
        request.AnomalyScore);

    var isCritical = request.AnomalyScore > 0.8 || string.Equals(request.Status, "Critical", StringComparison.OrdinalIgnoreCase);
    var decision = isCritical ? "CriticalAlert" : "Normal";
    var ticketId = isCritical ? $"INC-{Guid.NewGuid():N}"[..12].ToUpperInvariant() : string.Empty;
    var message = isCritical
        ? $"Critical anomaly detected for device {request.DeviceId}. Incident escalated: {ticketId}."
        : $"Telemetry from device {request.DeviceId} validated within safe operating tolerances.";

    var response = new TelemetryProcessResponse(
        DeviceId: request.DeviceId,
        Decision: decision,
        IncidentTicketId: ticketId,
        ResolvedAt: DateTimeOffset.UtcNow,
        Message: message);

    return Results.Ok(response);
});

await app.RunAsync();
