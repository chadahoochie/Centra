using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry: exports the control plane's baked-in ActivitySource ("Centra") and Meters
// ("Centra", "Centra.ControlPlane") over OTLP to the collector - see samples/DockerStack/otel.
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "control-plane"))
    .WithTracing(tracing => tracing
        .AddSource("Centra")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter("Centra")
        .AddMeter("Centra.ControlPlane")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .UseOtlpExporter();

builder.Services.AddCentraControlPlane();

var app = builder.Build();

app.MapCentraControlPlaneEndpoints();

app.Run();

public partial class Program { }
