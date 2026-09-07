using System.Diagnostics;
using System.Net;
using System.Text;
using Centra.Bindings;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Sample.Bindings.Handlers;
using Centra.Sample.Bindings.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Centra.Tests.Integration.Bindings;

public sealed class BindingsWorkflowIntegrationTests
{
    [Fact]
    public async Task Should_Execute_End_To_End_Bindings_Simulation_Successfully()
    {
        // Act
        var result = await BindingsDemoRunner.RunAsync();

        // Assert
        result.ShouldNotBeNull();
        result.CronJobTriggered.ShouldBeTrue();
        result.CronIterationsCompleted.ShouldBeGreaterThanOrEqualTo(2);
        result.OutputBindingDispatched.ShouldBeTrue();
        result.OutputStatusCode.ShouldBe("200");
        result.InputBindingTriggerHandled.ShouldBeTrue();
        result.InputResponseText.ShouldContain("acknowledged");
        result.DistributedCoordinationHonored.ShouldBeTrue();
        result.ElapsedDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Should_Route_Input_Binding_Trigger_With_TraceContext()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddCentra();
        builder.Services.AddCentraInMemory();
        builder.Services.AddCentraInputBindingHandler<OrdersWebhookTriggerHandler>("webhooks-orders");

        var app = builder.Build();
        app.MapCentraEndpoints();
        await app.StartAsync();

        var client = app.GetTestServer().CreateClient();

        using var parentActivity = new Activity("IntegrationTestCaller").Start();
        var traceparent = parentActivity.Id!;

        var requestContent = new StringContent(
            "{\"OrderId\":\"ORD-999\",\"Amount\":150.00,\"Customer\":\"Acme\"}",
            Encoding.UTF8,
            "application/json");

        requestContent.Headers.Add("traceparent", traceparent);

        var response = await client.PostAsync("/centra/bindings/webhooks-orders", requestContent);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("X-Centra-Processed").ShouldBeTrue();

        await app.StopAsync();
        await app.DisposeAsync();
    }
}
