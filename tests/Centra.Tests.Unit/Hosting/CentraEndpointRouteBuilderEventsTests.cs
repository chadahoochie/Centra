using System.Net;
using System.Text;
using Centra.Hosting.Extensions;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraEndpointRouteBuilderEventsTests
{
    [Theory]
    [InlineData(EventHandlingResult.Success, HttpStatusCode.OK)]
    [InlineData(EventHandlingResult.Drop, HttpStatusCode.OK)]
    [InlineData(EventHandlingResult.DeadLetter, HttpStatusCode.OK)]
    [InlineData(EventHandlingResult.Retry, HttpStatusCode.InternalServerError)]
    public async Task MapCentraEndpoints_Routes_To_Topic_Handler_And_Returns_Expected_Status(
        EventHandlingResult result,
        HttpStatusCode expectedStatus)
    {
        var handler = new StubTopicHandler { ResultToReturn = result };

        var reg = new CentraTopicRegistration(
            pubSubName: "pubsub-test",
            topic: "orders.created",
            eventType: typeof(string),
            handlerType: typeof(StubTopicHandler),
            invoker: (h, payload, headers, ct) => Task.FromResult(((StubTopicHandler)h).ResultToReturn));

        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSingleton(reg);
                    services.AddSingleton(handler);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapCentraEndpoints());
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var content = new StringContent("payload-data", Encoding.UTF8, "text/plain");
        var response = await client.PostAsync("/centra/events/pubsub-test/orders.created", content);

        response.StatusCode.ShouldBe(expectedStatus);
    }

    [Fact]
    public async Task MapCentraEndpoints_Returns_NotFound_When_Handler_Not_Registered_In_DI()
    {
        var reg = new CentraTopicRegistration(
            pubSubName: "pubsub-test",
            topic: "missing.handler",
            eventType: typeof(string),
            handlerType: typeof(StubTopicHandler),
            invoker: (h, payload, headers, ct) => Task.FromResult(EventHandlingResult.Success));

        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSingleton(reg);
                    // Do NOT register StubTopicHandler
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapCentraEndpoints());
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var content = new StringContent("data", Encoding.UTF8, "text/plain");
        var response = await client.PostAsync("/centra/events/pubsub-test/missing.handler", content);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapCentraEndpoints_Returns_500_When_Invoker_Throws_Exception()
    {
        var handler = new StubTopicHandler();
        var reg = new CentraTopicRegistration(
            pubSubName: "pubsub-test",
            topic: "failing.handler",
            eventType: typeof(string),
            handlerType: typeof(StubTopicHandler),
            invoker: (h, payload, headers, ct) => throw new InvalidOperationException("Handler exploded"));

        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSingleton(reg);
                    services.AddSingleton(handler);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapCentraEndpoints());
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var content = new StringContent("data", Encoding.UTF8, "text/plain");
        var response = await client.PostAsync("/centra/events/pubsub-test/failing.handler", content);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    public sealed class StubTopicHandler
    {
        public EventHandlingResult ResultToReturn { get; set; } = EventHandlingResult.Success;
    }
}
