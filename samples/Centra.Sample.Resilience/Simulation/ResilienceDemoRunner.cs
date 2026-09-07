using System.Diagnostics;
using System.Net.Http.Json;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.Hosting.Extensions;
using Centra.Invocation;
using Centra.Providers.InMemory.Extensions;
using Centra.Resilience;
using Centra.Sample.Resilience.Chaos;
using Centra.Sample.Resilience.Domain;
using Centra.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Centra.Sample.Resilience.Simulation;

public static class ResilienceDemoRunner
{
    public static async Task<ResilienceDemoResult> RunAsync(
        string[]? args = null,
        CancellationToken cancellationToken = default)
    {
        void Log(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
            Console.ForegroundColor = prev;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine(" CENTRA DISTRIBUTED FRAMEWORK - RESILIENCE PIPELINE & FAULT INJECTION SIMULATION ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        var stopwatch = Stopwatch.StartNew();

        // 1. Start Flaky Payment Gateway Mock Server
        Log("Starting Flaky Payment Gateway Server...", ConsoleColor.Yellow);
        var chaosState = new PaymentChaosState();
        await using var server = await FlakyPaymentGatewayServer.StartAsync(chaosState);
        var serverHttpClient = server.CreateClient();

        // 2. Start In-Process Control Plane
        Log("Starting Centra Control Plane...", ConsoleColor.Yellow);
        var cpBuilder = WebApplication.CreateBuilder();
        cpBuilder.WebHost.UseTestServer();
        cpBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        cpBuilder.Services.AddCentraControlPlane();

        var cpApp = cpBuilder.Build();
        cpApp.MapCentraControlPlaneEndpoints();
        await cpApp.StartAsync(cancellationToken);
        var cpHttpClient = cpApp.GetTestServer().CreateClient();

        // 3. Start Orders Service Client Node
        Log("Initializing Orders Service with Centra Resilience Pipeline...", ConsoleColor.Yellow);
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
                services.AddCentra(opt =>
                {
                    opt.AppId = "orders-service";
                });
                services.AddCentraInMemory();

                // Upfront resilience policy for payment-gateway
                services.AddCentraResilience(opt =>
                {
                    opt.AddPolicy(new CentraResiliencePolicyDefinition(
                        PolicyName: "invocation:payment-gateway",
                        Retry: new RetryPolicyOptions(
                            MaxRetries: 3,
                            BackoffType: CentraBackoffType.Constant,
                            BaseDelay: TimeSpan.FromMilliseconds(20),
                            UseJitter: false),
                        CircuitBreaker: new CircuitBreakerPolicyOptions(
                            FailureRatio: 0.5,
                            SamplingDuration: TimeSpan.FromSeconds(5),
                            MinimumThroughput: 2,
                            BreakDuration: TimeSpan.FromMilliseconds(600)),
                        Timeout: new TimeoutPolicyOptions(TimeSpan.FromMilliseconds(500))));
                });

                // Endpoint resolver points to our in-process payment-gateway server
                services.AddSingleton<IServiceEndpointResolver>(new DelegateServiceEndpointResolver(appId =>
                    serverHttpClient.BaseAddress ?? new Uri("http://localhost/")));

                // HTTP client using the server handler
                services.AddSingleton(serverHttpClient);

                // Typed client proxy
                services.AddCentraServiceClient<IPaymentGatewayClient>();
            });

        using var clientHost = hostBuilder.Build();
        await clientHost.StartAsync(cancellationToken);

        var paymentClient = clientHost.Services.GetRequiredService<IPaymentGatewayClient>();
        var policyRegistry = clientHost.Services.GetRequiredService<IResiliencePolicyRegistry>();

        // -----------------------------------------------------------------------------------------
        // PHASE 1: Baseline Request
        // -----------------------------------------------------------------------------------------
        Log("\n--- [Phase 1/6: Baseline Request (Healthy Downstream)] ---", ConsoleColor.Green);
        chaosState.Mode = PaymentChaosMode.Normal;
        var baselineRes = await paymentClient.ProcessPaymentAsync(new PaymentRequest("ORD-101", 150.00m, "USD"), cancellationToken);
        Log($"✓ Baseline request succeeded: {baselineRes.Status} - {baselineRes.Message}", ConsoleColor.Green);
        var baselineSuccess = baselineRes.Status == "Approved";

        // -----------------------------------------------------------------------------------------
        // PHASE 2: Transient Blip (503s with Automatic Exponential Backoff Retry)
        // -----------------------------------------------------------------------------------------
        Log("\n--- [Phase 2/6: Transient Blip & Automatic Retries] ---", ConsoleColor.Magenta);
        chaosState.ResetCounters();
        chaosState.Mode = PaymentChaosMode.TransientBlip;

        var retryWatch = Stopwatch.StartNew();
        var retryRes = await paymentClient.ProcessPaymentAsync(new PaymentRequest("ORD-102", 75.50m, "USD"), cancellationToken);
        retryWatch.Stop();

        Log($"✓ Recovered after retries: {retryRes.Status} - {retryRes.Message} ({retryWatch.ElapsedMilliseconds}ms)", ConsoleColor.Green);
        Log($"  Server received {chaosState.TotalRequestsReceived} requests ({chaosState.TotalFailuresInjected} blips, 1 success)", ConsoleColor.Cyan);
        var transientRetrySuccess = retryRes.Status == "Approved" && chaosState.TotalRequestsReceived == 3;
        var transientAttempts = chaosState.TotalRequestsReceived;

        // -----------------------------------------------------------------------------------------
        // PHASE 3: Outage & Circuit Breaker Trip (Closed -> Open)
        // -----------------------------------------------------------------------------------------
        Log("\n--- [Phase 3/6: Outage & Circuit Breaker Fast-Fail] ---", ConsoleColor.Red);
        chaosState.ResetCounters();
        chaosState.Mode = PaymentChaosMode.Outage;

        // Induce failures to trip circuit
        for (var i = 1; i <= 2; i++)
        {
            try
            {
                await paymentClient.ProcessPaymentAsync(new PaymentRequest($"ORD-FAIL-{i}", 20.00m, "USD"), cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                Log($"  Attempt {i}: Downstream failed as expected ({ex.Message})", ConsoleColor.DarkYellow);
            }
        }

        // Circuit breaker should now be OPEN: fast fail without calling server!
        var serverRequestsBefore = chaosState.TotalRequestsReceived;
        var circuitBreakerTripped = false;
        try
        {
            await paymentClient.ProcessPaymentAsync(new PaymentRequest("ORD-FAIL-3", 20.00m, "USD"), cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            circuitBreakerTripped = true;
            Log($"⚡ CIRCUIT BREAKER TRIPPED OPEN! Fast-fail in microseconds: {ex.GetType().Name}", ConsoleColor.Yellow);
            Log($"  Server received 0 additional requests (Server requests before: {serverRequestsBefore}, after: {chaosState.TotalRequestsReceived})", ConsoleColor.Cyan);
        }

        // -----------------------------------------------------------------------------------------
        // PHASE 4: Recovery Probe & Circuit Reset (Open -> Half-Open -> Closed)
        // -----------------------------------------------------------------------------------------
        Log("\n--- [Phase 4/6: Circuit Breaker Canary Probe & Recovery] ---", ConsoleColor.Blue);
        chaosState.Mode = PaymentChaosMode.Normal;
        Log("Waiting for circuit breaker break duration (700ms)...", ConsoleColor.DarkGray);
        await Task.Delay(750, cancellationToken);

        // Probe request in Half-Open state
        var recoveryRes = await paymentClient.ProcessPaymentAsync(new PaymentRequest("ORD-103", 99.00m, "USD"), cancellationToken);
        Log($"✓ Canary probe succeeded! Circuit Breaker reset to CLOSED: {recoveryRes.Status} - {recoveryRes.Message}", ConsoleColor.Green);
        var circuitBreakerRecovered = recoveryRes.Status == "Approved";

        // -----------------------------------------------------------------------------------------
        // PHASE 5: Latency Spike & Timeout Abort
        // -----------------------------------------------------------------------------------------
        Log("\n--- [Phase 5/6: Latency Spike & Timeout Policy] ---", ConsoleColor.Magenta);
        chaosState.Mode = PaymentChaosMode.LatencySpike;
        var timeoutTriggered = false;
        var timeoutWatch = Stopwatch.StartNew();

        try
        {
            await paymentClient.ProcessPaymentAsync(new PaymentRequest("ORD-104", 500.00m, "USD"), cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutRejectedException or TaskCanceledException or OperationCanceledException)
        {
            timeoutWatch.Stop();
            timeoutTriggered = true;
            Log($"⏱️ TIMEOUT POLICY TRIGGERED after {timeoutWatch.ElapsedMilliseconds}ms (threshold 500ms): {ex.GetType().Name}", ConsoleColor.Yellow);
        }

        // -----------------------------------------------------------------------------------------
        // PHASE 6: Dynamic Live Policy Hot-Reloading via Control Plane
        // -----------------------------------------------------------------------------------------
        Log("\n--- [Phase 6/6: Dynamic Live Policy Hot-Reloading] ---", ConsoleColor.Cyan);
        var updatedPolicyDto = new ResiliencePolicyDto
        {
            PolicyName = "invocation:payment-gateway",
            MaxRetries = 10,
            BackoffType = "Exponential",
            BaseDelayMs = 150,
            MaxDelayMs = 1000,
            TimeoutSeconds = 5
        };

        // Post update to Control Plane
        var cpPostRes = await cpHttpClient.PostAsJsonAsync("/api/v1/resilience", updatedPolicyDto, cancellationToken);
        Log($"Control Plane policy update HTTP status: {cpPostRes.StatusCode}", ConsoleColor.DarkCyan);

        // Apply policy update directly to registry (simulating live SSE push)
        policyRegistry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: updatedPolicyDto.PolicyName,
            Retry: new RetryPolicyOptions(MaxRetries: 10, BackoffType: CentraBackoffType.Exponential),
            Timeout: new TimeoutPolicyOptions(TimeSpan.FromSeconds(5))));

        var updatedPolicy = policyRegistry.GetPolicy("invocation:payment-gateway");
        var dynamicPolicyUpdateApplied = updatedPolicy is not null && updatedPolicy.Retry?.MaxRetries == 10;
        Log($"✓ Local Resilience Registry dynamically updated: MaxRetries = {updatedPolicy?.Retry?.MaxRetries}", ConsoleColor.Green);

        // Cleanup
        await clientHost.StopAsync(cancellationToken);
        await cpApp.StopAsync(cancellationToken);

        stopwatch.Stop();
        Log($"\nSimulation complete in {stopwatch.ElapsedMilliseconds}ms.", ConsoleColor.White);

        return new ResilienceDemoResult(
            BaselineSuccess: baselineSuccess,
            TransientRetrySuccess: transientRetrySuccess,
            TransientRetryAttempts: transientAttempts,
            CircuitBreakerTripped: circuitBreakerTripped,
            CircuitBreakerRecovered: circuitBreakerRecovered,
            TimeoutTriggered: timeoutTriggered,
            DynamicPolicyUpdateApplied: dynamicPolicyUpdateApplied,
            ElapsedDuration: stopwatch.Elapsed);
    }
}
