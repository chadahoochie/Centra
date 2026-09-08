using Centra.Sample.DockerStack.Simulator;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "dockerstack-simulator"))
    .WithTracing(tracing => tracing
        .AddSource(SimulationWorker.ActivitySourceName)
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .UseOtlpExporter();

builder.Services.AddSingleton(SimulationOptions.FromConfiguration(builder.Configuration));
builder.Services.AddHostedService<SimulationWorker>();

var host = builder.Build();
host.Run();
