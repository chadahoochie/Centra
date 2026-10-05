using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Centra.Events;
using Centra.Invocation;
using Centra.Providers.Flotilla.Grpc.Client;
using Centra.Providers.Flotilla.Grpc.Options;
using Centra.Providers.Flotilla.PubSub;
using Centra.Providers.Flotilla.Tcp.Client;
using Centra.Providers.Flotilla.Tcp.Options;
using Centra.Providers.Flotilla.Udp.Client;
using Centra.Providers.Flotilla.Udp.Options;
using Centra.PubSub;
using Centra.Sample.FlotillaSimulation.Contracts.Clients;
using Centra.Sample.FlotillaSimulation.Contracts.Models;
using Centra.Sample.FlotillaSimulation.Producer.Services;
using Centra.Sample.FlotillaSimulation.Server.Consensus;
using Centra.Sample.FlotillaSimulation.Server.Transports;
using Centra.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.Providers;

public sealed class FlotillaServiceInvocationIntegrationTests : IAsyncLifetime
{
    private TestServer? _apiServer;
    private HttpClient? _apiHttpClient;
    private TestServer? _grpcServer;
    private FlotillaConsensusEngine? _engine;
    private FlotillaConsensusMetrics? _metrics;
    private FlotillaTcpServer? _tcpServer;
    private FlotillaUdpServer? _udpServer;
    private CancellationTokenSource? _serverCts;
    private int _tcpPort;
    private int _udpPort;
    private volatile string? _lastReceivedTraceparent;
    private ActivityListener? _activityListener;
    private readonly List<Activity> _recordedActivities = new();

    public async Task InitializeAsync()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Centra",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                lock (_recordedActivities)
                {
                    _recordedActivities.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(_activityListener);

        // 1. Build and start in-memory TestServer simulating flotilla-api
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var app = builder.Build();
        app.MapPost("/telemetry/process", (HttpContext httpContext, TelemetryProcessRequest request) =>
        {
            _lastReceivedTraceparent = httpContext.Request.Headers["traceparent"].ToString();

            var isCritical = request.AnomalyScore > 0.8 || string.Equals(request.Status, "Critical", StringComparison.OrdinalIgnoreCase);
            var decision = isCritical ? "CriticalAlert" : "Normal";
            var ticketId = isCritical ? $"INC-{Guid.NewGuid():N}"[..12].ToUpperInvariant() : string.Empty;
            var message = isCritical
                ? $"Critical anomaly detected for device {request.DeviceId}. Escalated ticket {ticketId}."
                : $"Telemetry from device {request.DeviceId} validated within safe bounds.";

            var response = new TelemetryProcessResponse(
                DeviceId: request.DeviceId,
                Decision: decision,
                IncidentTicketId: ticketId,
                ResolvedAt: DateTimeOffset.UtcNow,
                Message: message);

            return Results.Ok(response);
        });

        await app.StartAsync();
        _apiServer = app.GetTestServer();
        _apiHttpClient = _apiServer.CreateClient();

        // 2. Allocate available dynamic TCP & UDP ports
        var tempListener = new TcpListener(IPAddress.Loopback, 0);
        tempListener.Start();
        _tcpPort = ((IPEndPoint)tempListener.LocalEndpoint).Port;
        tempListener.Stop();

        // 3. Initialize Consensus Engine, Metrics, and Background Transport Servers
        _engine = new FlotillaConsensusEngine();
        _metrics = new FlotillaConsensusMetrics();
        _serverCts = new CancellationTokenSource();

        var tcpConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Flotilla:TcpPort"] = _tcpPort.ToString()
            })
            .Build();
        _tcpServer = new FlotillaTcpServer(_engine, _metrics, tcpConfig, NullLogger<FlotillaTcpServer>.Instance);
        _ = _tcpServer.StartAsync(_serverCts.Token);

        var udpConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Flotilla:UdpPort"] = "0"
            })
            .Build();
        _udpServer = new FlotillaUdpServer(_engine, _metrics, udpConfig, NullLogger<FlotillaUdpServer>.Instance);
        _udpPort = _udpServer.Port;
        _ = _udpServer.StartAsync(_serverCts.Token);

        // 4. Start gRPC TestServer
        var grpcBuilder = WebApplication.CreateBuilder();
        grpcBuilder.WebHost.UseTestServer();
        grpcBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        grpcBuilder.Services.AddGrpc();
        grpcBuilder.Services.AddSingleton(_engine);
        grpcBuilder.Services.AddSingleton(_metrics);

        var grpcApp = grpcBuilder.Build();
        grpcApp.MapGrpcService<FlotillaGrpcServerService>();
        await grpcApp.StartAsync();
        _grpcServer = grpcApp.GetTestServer();

        // Allow TCP & UDP servers to bind and start accepting sockets
        await Task.Delay(100);
    }

    public async Task DisposeAsync()
    {
        if (_serverCts is not null)
        {
            _serverCts.Cancel();
            if (_tcpServer is not null)
            {
                await _tcpServer.StopAsync(CancellationToken.None);
                _tcpServer.Dispose();
            }
            if (_udpServer is not null)
            {
                await _udpServer.StopAsync(CancellationToken.None);
                _udpServer.Dispose();
            }
            _serverCts.Dispose();
        }

        _grpcServer?.Dispose();
        _apiHttpClient?.Dispose();
        _apiServer?.Dispose();
        _activityListener?.Dispose();
    }

    [Fact]
    public async Task Should_Flow_EndToEnd_From_Bogus_Producer_Through_Flotilla_TCP_To_Consumer_And_Service_Invocation()
    {
        _apiHttpClient.ShouldNotBeNull();
        _engine.ShouldNotBeNull();

        // 1. Setup Centra Service Invoker connected to TestServer
        var invoker = new CentraServiceInvoker(_apiHttpClient);
        var telemetryApiClient = ServiceProxyFactory.Create<ITelemetryApiClient>(invoker);

        // 2. Setup TCS to capture the end-to-end invocation response
        var completionTcs = new TaskCompletionSource<TelemetryProcessResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 3. Create Flotilla TCP Client & Driver
        var options = Microsoft.Extensions.Options.Options.Create(new FlotillaTcpOptions
        {
            ClusterNodes = [$"127.0.0.1:{_tcpPort}"],
            ClientTimeoutMs = 2000,
            EnableChecksumVerification = true
        });

        await using var consumerClient = new FlotillaTcpClient(options);
        await using var consumerPubSubDriver = new FlotillaPubSubDriver(consumerClient);

        // Start subscription worker on consumer client
        using var workerCts = new CancellationTokenSource();
        var worker = new FlotillaSubscriptionWorker(consumerClient, consumerPubSubDriver);
        _ = worker.StartAsync(workerCts.Token);

        // 4. Subscribe consumer handler to 'telemetry.v1'
        await consumerPubSubDriver.SubscribeAsync("pubsub", "telemetry.v1", async (payload, headers, ct) =>
        {
            try
            {
                var telemetry = JsonCentraSerializer.Default.Deserialize<TelemetryEvent>(payload);
                if (telemetry is null)
                {
                    return EventHandlingResult.Drop;
                }

                var anomalyScore = Math.Round((telemetry.Temperature / 100.0 * 0.4) + (telemetry.Vibration / 10.0 * 0.6), 3);
                var request = new TelemetryProcessRequest(
                    DeviceId: telemetry.DeviceId,
                    Status: telemetry.Status,
                    AnomalyScore: anomalyScore,
                    ProcessedAt: DateTimeOffset.UtcNow);

                var response = await telemetryApiClient.ProcessTelemetryAsync(request, ct);
                completionTcs.TrySetResult(response);
                return EventHandlingResult.Success;
            }
            catch (Exception ex)
            {
                completionTcs.TrySetException(ex);
                return EventHandlingResult.Retry;
            }
        });

        // Allow consumer TCP subscriber to connect and register with Flotilla server
        await Task.Delay(200);

        // 5. Generate Bogus telemetry event
        var generator = new BogusTelemetryGenerator();
        var bogusTelemetry = generator.Generate();
        bogusTelemetry.DeviceId.ShouldNotBeNullOrWhiteSpace();

        // 6. Pack CloudEvent headers with distributed W3C traceparent and publish to Flotilla via producer
        var expectedTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        var telemetryBytes = JsonCentraSerializer.Default.Serialize(bogusTelemetry);
        var headers = new Dictionary<string, string>
        {
            ["ce-id"] = Guid.NewGuid().ToString("N"),
            ["ce-type"] = "telemetry.v1",
            ["ce-source"] = "centra://flotilla-producer",
            ["ce-specversion"] = "1.0",
            ["ce-correlationid"] = "corr-flotilla-001",
            ["traceparent"] = $"00-{expectedTraceId}-00f067aa0ba902b7-01"
        };

        await using var producerClient = new FlotillaTcpClient(options);
        await using var producerPubSubDriver = new FlotillaPubSubDriver(producerClient);
        await producerPubSubDriver.PublishAsync("pubsub", "telemetry.v1", telemetryBytes, headers);

        // 7. Await end-to-end receipt and invocation
        var completedTask = await Task.WhenAny(completionTcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completedTask.ShouldBe(completionTcs.Task, "Timed out waiting for consumer to process message and invoke API");

        var apiResponse = await completionTcs.Task;
        apiResponse.ShouldNotBeNull();
        apiResponse.DeviceId.ShouldBe(bogusTelemetry.DeviceId);
        apiResponse.Decision.ShouldNotBeNullOrWhiteSpace();
        apiResponse.Message.ShouldContain(bogusTelemetry.DeviceId);

        // 8. Assert end-to-end W3C trace context propagation through to API invocation
        _lastReceivedTraceparent.ShouldNotBeNullOrWhiteSpace();
        _lastReceivedTraceparent.ShouldContain(expectedTraceId);

        lock (_recordedActivities)
        {
            var serverActivity = _recordedActivities.FirstOrDefault(a => a.OperationName == "Flotilla.Server.Propose" && a.TraceId.ToString() == expectedTraceId);
            serverActivity.ShouldNotBeNull();

            var processActivity = _recordedActivities.FirstOrDefault(a => a.OperationName == "Centra.PubSub.Process" && a.TraceId.ToString() == expectedTraceId);
            processActivity.ShouldNotBeNull();
            processActivity.ParentSpanId.ShouldBe(serverActivity.SpanId);
        }

        // 9. Assert Flotilla consensus server metrics
        _metrics.ShouldNotBeNull();
        _metrics.TotalProposals.ShouldBeGreaterThan(0);
        _metrics.CommittedEntries.ShouldBeGreaterThan(0);
        var prometheusText = _metrics.ToPrometheusText();
        prometheusText.ShouldContain("flotilla_server_proposals_total");
        prometheusText.ShouldContain("flotilla_server_commits_total");
        prometheusText.ShouldContain("flotilla_server_tcp_proposals_total");

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Should_Flow_EndToEnd_From_Bogus_Producer_Through_Flotilla_gRPC_To_Consumer_And_Service_Invocation()
    {
        _apiHttpClient.ShouldNotBeNull();
        _grpcServer.ShouldNotBeNull();

        // 1. Setup Centra Service Invoker connected to TestServer
        var invoker = new CentraServiceInvoker(_apiHttpClient);
        var telemetryApiClient = ServiceProxyFactory.Create<ITelemetryApiClient>(invoker);

        // 2. Setup TCS to capture the end-to-end invocation response
        var completionTcs = new TaskCompletionSource<TelemetryProcessResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 3. Create Flotilla gRPC Client & Driver using the in-memory test server handler
        var options = Microsoft.Extensions.Options.Options.Create(new FlotillaGrpcOptions
        {
            ClusterNodes = [_grpcServer.BaseAddress.ToString()],
            ClientTimeoutMs = 5000,
        });

        await using var consumerClient = new FlotillaGrpcClient(options, null, _grpcServer.CreateHandler());
        await using var consumerPubSubDriver = new FlotillaPubSubDriver(consumerClient);

        // Start subscription worker on consumer client
        using var workerCts = new CancellationTokenSource();
        var worker = new FlotillaSubscriptionWorker(consumerClient, consumerPubSubDriver);
        _ = worker.StartAsync(workerCts.Token);

        // 4. Subscribe consumer handler to 'telemetry.v1'
        await consumerPubSubDriver.SubscribeAsync("pubsub", "telemetry.v1", async (payload, headers, ct) =>
        {
            try
            {
                var telemetry = JsonCentraSerializer.Default.Deserialize<TelemetryEvent>(payload);
                if (telemetry is null)
                {
                    return EventHandlingResult.Drop;
                }

                var anomalyScore = Math.Round((telemetry.Temperature / 100.0 * 0.4) + (telemetry.Vibration / 10.0 * 0.6), 3);
                var request = new TelemetryProcessRequest(
                    DeviceId: telemetry.DeviceId,
                    Status: telemetry.Status,
                    AnomalyScore: anomalyScore,
                    ProcessedAt: DateTimeOffset.UtcNow);

                var response = await telemetryApiClient.ProcessTelemetryAsync(request, ct);
                completionTcs.TrySetResult(response);
                return EventHandlingResult.Success;
            }
            catch (Exception ex)
            {
                completionTcs.TrySetException(ex);
                return EventHandlingResult.Retry;
            }
        });

        // 5. Generate Bogus telemetry event
        var generator = new BogusTelemetryGenerator();
        var bogusTelemetry = generator.Generate();
        bogusTelemetry.DeviceId.ShouldNotBeNullOrWhiteSpace();

        // 6. Pack CloudEvent headers with distributed W3C traceparent and publish to Flotilla via producer
        var expectedTraceId = "5cf92f3577b34da6a3ce929d0e0e4736";
        var telemetryBytes = JsonCentraSerializer.Default.Serialize(bogusTelemetry);
        var headers = new Dictionary<string, string>
        {
            ["ce-id"] = Guid.NewGuid().ToString("N"),
            ["ce-type"] = "telemetry.v1",
            ["ce-source"] = "centra://flotilla-grpc-producer",
            ["ce-specversion"] = "1.0",
            ["ce-correlationid"] = "corr-flotilla-grpc-001",
            ["traceparent"] = $"00-{expectedTraceId}-00f067aa0ba902b7-02"
        };

        await using var producerClient = new FlotillaGrpcClient(options, null, _grpcServer.CreateHandler());
        await using var producerPubSubDriver = new FlotillaPubSubDriver(producerClient);
        await producerPubSubDriver.PublishAsync("pubsub", "telemetry.v1", telemetryBytes, headers);

        // 7. Await end-to-end receipt and invocation
        var completedTask = await Task.WhenAny(completionTcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completedTask.ShouldBe(completionTcs.Task, "Timed out waiting for gRPC consumer to process message and invoke API");

        var apiResponse = await completionTcs.Task;
        apiResponse.ShouldNotBeNull();
        apiResponse.DeviceId.ShouldBe(bogusTelemetry.DeviceId);
        apiResponse.Decision.ShouldNotBeNullOrWhiteSpace();
        apiResponse.Message.ShouldContain(bogusTelemetry.DeviceId);

        // 8. Assert end-to-end W3C trace context propagation through to API invocation
        _lastReceivedTraceparent.ShouldNotBeNullOrWhiteSpace();
        _lastReceivedTraceparent.ShouldContain(expectedTraceId);

        lock (_recordedActivities)
        {
            var serverActivity = _recordedActivities.FirstOrDefault(a => a.OperationName == "Flotilla.Server.Propose" && a.TraceId.ToString() == expectedTraceId);
            serverActivity.ShouldNotBeNull();

            var processActivity = _recordedActivities.FirstOrDefault(a => a.OperationName == "Centra.PubSub.Process" && a.TraceId.ToString() == expectedTraceId);
            processActivity.ShouldNotBeNull();
            processActivity.ParentSpanId.ShouldBe(serverActivity.SpanId);
        }

        // 9. Assert Flotilla consensus server metrics
        _metrics.ShouldNotBeNull();
        _metrics.TotalProposals.ShouldBeGreaterThan(0);
        var prometheusText = _metrics.ToPrometheusText();
        prometheusText.ShouldContain("flotilla_server_proposals_total");
        prometheusText.ShouldContain("flotilla_server_grpc_proposals_total");

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Should_Flow_EndToEnd_From_Bogus_Producer_Through_Flotilla_UDP_To_Consumer_And_Service_Invocation()
    {
        _apiHttpClient.ShouldNotBeNull();
        _engine.ShouldNotBeNull();

        // 1. Setup Centra Service Invoker connected to TestServer
        var invoker = new CentraServiceInvoker(_apiHttpClient);
        var telemetryApiClient = ServiceProxyFactory.Create<ITelemetryApiClient>(invoker);

        // 2. Setup TCS to capture the end-to-end invocation response
        var completionTcs = new TaskCompletionSource<TelemetryProcessResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 3. Create Flotilla UDP Client & Driver
        var options = Microsoft.Extensions.Options.Options.Create(new FlotillaUdpOptions
        {
            ClusterNodes = [$"127.0.0.1:{_udpPort}"],
            ClientTimeoutMs = 2000
        });

        await using var udpClient = new FlotillaUdpClient(options);
        await using var pubSubDriver = new FlotillaPubSubDriver(udpClient);

        // Start subscription worker
        using var workerCts = new CancellationTokenSource();
        var worker = new FlotillaSubscriptionWorker(udpClient, pubSubDriver);
        _ = worker.StartAsync(workerCts.Token);

        // 4. Subscribe consumer handler to 'telemetry.v1'
        await pubSubDriver.SubscribeAsync("pubsub", "telemetry.v1", async (payload, headers, ct) =>
        {
            try
            {
                var telemetry = JsonCentraSerializer.Default.Deserialize<TelemetryEvent>(payload);
                if (telemetry is null)
                {
                    return EventHandlingResult.Drop;
                }

                var anomalyScore = Math.Round((telemetry.Temperature / 100.0 * 0.4) + (telemetry.Vibration / 10.0 * 0.6), 3);
                var request = new TelemetryProcessRequest(
                    DeviceId: telemetry.DeviceId,
                    Status: telemetry.Status,
                    AnomalyScore: anomalyScore,
                    ProcessedAt: DateTimeOffset.UtcNow);

                var response = await telemetryApiClient.ProcessTelemetryAsync(request, ct);
                completionTcs.TrySetResult(response);
                return EventHandlingResult.Success;
            }
            catch (Exception ex)
            {
                completionTcs.TrySetException(ex);
                return EventHandlingResult.Retry;
            }
        });

        // 5. Generate Bogus telemetry event
        var generator = new BogusTelemetryGenerator();
        var bogusTelemetry = generator.Generate();
        bogusTelemetry.DeviceId.ShouldNotBeNullOrWhiteSpace();

        // 6. Pack CloudEvent headers with distributed W3C traceparent and publish to Flotilla
        var expectedTraceId = "6cf92f3577b34da6a3ce929d0e0e4736";
        var telemetryBytes = JsonCentraSerializer.Default.Serialize(bogusTelemetry);
        var headers = new Dictionary<string, string>
        {
            ["ce-id"] = Guid.NewGuid().ToString("N"),
            ["ce-type"] = "telemetry.v1",
            ["ce-source"] = "centra://flotilla-udp-producer",
            ["ce-specversion"] = "1.0",
            ["ce-correlationid"] = "corr-flotilla-udp-001",
            ["traceparent"] = $"00-{expectedTraceId}-00f067aa0ba902b7-03"
        };

        await pubSubDriver.PublishAsync("pubsub", "telemetry.v1", telemetryBytes, headers);

        // 7. Await end-to-end receipt and invocation
        var completedTask = await Task.WhenAny(completionTcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completedTask.ShouldBe(completionTcs.Task, "Timed out waiting for UDP consumer to process message and invoke API");

        var apiResponse = await completionTcs.Task;
        apiResponse.ShouldNotBeNull();
        apiResponse.DeviceId.ShouldBe(bogusTelemetry.DeviceId);
        apiResponse.Decision.ShouldNotBeNullOrWhiteSpace();
        apiResponse.Message.ShouldContain(bogusTelemetry.DeviceId);

        // 8. Assert end-to-end W3C trace context propagation through to API invocation
        _lastReceivedTraceparent.ShouldNotBeNullOrWhiteSpace();
        _lastReceivedTraceparent.ShouldContain(expectedTraceId);

        // 9. Assert Flotilla consensus server metrics (allow brief time for async UDP datagram socket receive)
        _metrics.ShouldNotBeNull();
        var sw = Stopwatch.StartNew();
        while (_metrics.TotalProposals == 0 && sw.ElapsedMilliseconds < 2000)
        {
            await Task.Delay(20);
        }
        _metrics.TotalProposals.ShouldBeGreaterThan(0);
        var prometheusText = _metrics.ToPrometheusText();
        prometheusText.ShouldContain("flotilla_server_proposals_total");
        prometheusText.ShouldContain("flotilla_server_udp_proposals_total");

        await worker.StopAsync(CancellationToken.None);
    }
}
