using Centra.Sample.FlotillaSimulation.Server.Consensus;
using Centra.Sample.FlotillaSimulation.Server.Transports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    .ConfigureResource(resource => resource.AddService(serviceName: "flotilla-server"))
    .WithTracing(tracing => tracing
        .AddSource("Centra")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter(FlotillaConsensusMetrics.MeterName)
        .AddMeter("Centra")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .UseOtlpExporter();

// Register Flotilla Consensus Engine and Metrics
builder.Services.AddSingleton<FlotillaConsensusEngine>();
builder.Services.AddSingleton<FlotillaConsensusMetrics>();

// Register Transport Listeners (TCP & UDP background listeners)
builder.Services.AddHostedService<FlotillaTcpServer>();
builder.Services.AddHostedService<FlotillaUdpServer>();

var grpcPort = builder.Configuration.GetValue("Flotilla:GrpcPort", 9300);
var httpPort = builder.Configuration.GetValue("Flotilla:HttpPort", 9301);

// Configure Kestrel to support HTTP/2 cleartext (h2c) for gRPC on GrpcPort and HTTP/1.1 on HttpPort
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(grpcPort, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
    options.ListenAnyIP(httpPort, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });
});

// Register gRPC Services
builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<FlotillaGrpcServerService>();

app.MapGet("/", (FlotillaConsensusEngine engine, IConfiguration config) => Results.Ok(new
{
    Service = "flotilla-server",
    Status = "Leader",
    Term = engine.CurrentTerm,
    CommitIndex = engine.CurrentCommitIndex,
    TcpPort = config.GetValue("Flotilla:TcpPort", 9100),
    UdpPort = config.GetValue("Flotilla:UdpPort", 9200),
    GrpcPort = grpcPort,
    HttpPort = config.GetValue("Flotilla:HttpPort", 9301)
}));

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));

app.MapGet("/metrics", (FlotillaConsensusMetrics metrics) =>
    Results.Text(metrics.ToPrometheusText(), "text/plain; version=0.0.4; charset=utf-8"));

await app.RunAsync();
