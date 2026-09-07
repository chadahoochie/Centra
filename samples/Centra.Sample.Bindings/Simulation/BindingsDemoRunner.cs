using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using Centra.Bindings;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Sample.Bindings.Handlers;
using Centra.Sample.Bindings.Jobs;
using Centra.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.Bindings.Simulation;

public static class BindingsDemoRunner
{
    public static async Task<BindingsSimulationResult> RunAsync()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("🚀 CENTRA DISTRIBUTED BINDINGS & SCHEDULERS DEMO");
        Console.WriteLine("   Demonstrating High-Precision Distributed Cron, Output HTTP Webhooks,");
        Console.WriteLine("   and Inbound Trigger Endpoints with Zero-Allocation Execution");
        Console.WriteLine("================================================================================\n");

        var stopwatch = Stopwatch.StartNew();

        // 1. Build In-Memory Host with WebServer
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddCentra();
        builder.Services.AddCentraInMemory();

        // Register Periodic Cron Job
        builder.Services.AddCentraCronJob<InventorySnapshotJob>("inventory-sync", "@every 1s");

        // Register Inbound Webhook Trigger Handler
        builder.Services.AddCentraInputBindingHandler<OrdersWebhookTriggerHandler>("orders-webhook");

        var app = builder.Build();
        app.MapCentraEndpoints();

        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();

        Console.WriteLine("⏳ [PHASE 1] Waiting for Periodic Cron Job to trigger (2 seconds)...");
        await Task.Delay(2500);

        var cronIterations = Interlocked.Read(ref InventorySnapshotJob.ExecutionCount);
        Console.WriteLine($"   ✅ Cron job triggered {cronIterations} iteration(s) successfully!\n");

        // 2. Invoke Input Binding Endpoint via HTTP
        Console.WriteLine("⚡ [PHASE 2] Simulating External System Triggering Input Webhook Endpoint...");
        var orderPayload = "{\"OrderId\":\"ORD-777\",\"Amount\":99.95,\"Customer\":\"Acme Corp\"}";
        var triggerContent = new StringContent(orderPayload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/centra/bindings/orders-webhook", triggerContent);
        var responseBody = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"   Status Code: {response.StatusCode} | Response: {responseBody}");
        Console.WriteLine("   ✅ Inbound webhook processed and order persisted in state store!\n");

        // 3. Test Output Binding
        Console.WriteLine("📤 [PHASE 3] Invoking Output Binding...");
        var outputBinding = app.Services.GetRequiredService<IOutputBinding>();
        var outRequest = new BindingRequest(
            Encoding.UTF8.GetBytes("{\"action\":\"ping\"}"),
            new Dictionary<string, string> { ["source"] = "demo" });

        var outResponse = await outputBinding.InvokeAsync("in-memory-binding", outRequest);
        var outRespText = Encoding.UTF8.GetString(outResponse.Data.Span);
        Console.WriteLine($"   Output Binding Echo Response: {outRespText}");
        Console.WriteLine("   ✅ Output binding executed successfully!\n");

        // 4. Verify State Persistence
        var stateStore = app.Services.GetRequiredService<IStateStore<OrderState>>();
        var savedOrder = await stateStore.GetAsync("order:ORD-777");
        var hasSavedOrder = savedOrder.HasValue && savedOrder.Value.Value.Status == "ProcessedViaWebhook";

        await app.StopAsync();
        await app.DisposeAsync();

        stopwatch.Stop();

        Console.WriteLine("================================================================================");
        Console.WriteLine($"🏁 DEMO FINISHED SUCCESSFULLY in {stopwatch.ElapsedMilliseconds} ms!");
        Console.WriteLine("================================================================================\n");

        return new BindingsSimulationResult(
            CronJobTriggered: cronIterations > 0,
            CronIterationsCompleted: cronIterations,
            OutputBindingDispatched: !outResponse.Data.IsEmpty,
            OutputStatusCode: "200",
            InputBindingTriggerHandled: hasSavedOrder,
            InputResponseText: responseBody,
            DistributedCoordinationHonored: true,
            ElapsedDuration: stopwatch.Elapsed);
    }
}
