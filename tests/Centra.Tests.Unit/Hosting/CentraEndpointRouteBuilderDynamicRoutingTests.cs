using System.Net;
using System.Text;
using Centra.Events;
using Centra.Hosting.Extensions;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Centra.PubSub.Routing.Rules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraEndpointRouteBuilderDynamicRoutingTests
{
    public sealed class OrderV1Handler
    {
        public bool Handled { get; set; }
    }

    public sealed class OrderV2Handler
    {
        public bool Handled { get; set; }
    }

    public sealed class DefaultOrderHandler
    {
        public bool Handled { get; set; }
    }

    [Fact]
    public async Task MapCentraEndpoints_MultipleRoutesOnSameTopic_RoutesBasedOnCloudEventHeaders()
    {
        var evaluator = new RuleFilterEvaluator();

        var v1Handler = new OrderV1Handler();
        var v2Handler = new OrderV2Handler();
        var defaultHandler = new DefaultOrderHandler();

        var regV1 = new CentraTopicRegistration(
            pubSubName: "pubsub-test",
            topic: "orders",
            eventType: typeof(string),
            handlerType: typeof(OrderV1Handler),
            deadLetterTopic: null,
            invoker: (h, payload, headers, ct) =>
            {
                ((OrderV1Handler)h).Handled = true;
                return Task.FromResult(EventHandlingResult.Success);
            },
            ruleFilter: "event.type == 'order.v1'",
            priority: 20,
            compiledFilter: evaluator.Compile("event.type == 'order.v1'"));

        var regV2 = new CentraTopicRegistration(
            pubSubName: "pubsub-test",
            topic: "orders",
            eventType: typeof(string),
            handlerType: typeof(OrderV2Handler),
            deadLetterTopic: null,
            invoker: (h, payload, headers, ct) =>
            {
                ((OrderV2Handler)h).Handled = true;
                return Task.FromResult(EventHandlingResult.Success);
            },
            ruleFilter: "event.type == 'order.v2'",
            priority: 10,
            compiledFilter: evaluator.Compile("event.type == 'order.v2'"));

        var regDefault = new CentraTopicRegistration(
            pubSubName: "pubsub-test",
            topic: "orders",
            eventType: typeof(string),
            handlerType: typeof(DefaultOrderHandler),
            deadLetterTopic: null,
            invoker: (h, payload, headers, ct) =>
            {
                ((DefaultOrderHandler)h).Handled = true;
                return Task.FromResult(EventHandlingResult.Success);
            });

        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSingleton(regV1);
                    services.AddSingleton(regV2);
                    services.AddSingleton(regDefault);
                    services.AddSingleton(v1Handler);
                    services.AddSingleton(v2Handler);
                    services.AddSingleton(defaultHandler);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapCentraEndpoints());
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // 1. Post V1 event
        var contentV1 = new StringContent("{}", Encoding.UTF8, "application/json");
        contentV1.Headers.Add(CloudEventConstants.TypeHeader, "order.v1");
        var res1 = await client.PostAsync("/centra/events/pubsub-test/orders", contentV1);
        res1.StatusCode.ShouldBe(HttpStatusCode.OK);
        v1Handler.Handled.ShouldBeTrue();
        v2Handler.Handled.ShouldBeFalse();
        defaultHandler.Handled.ShouldBeFalse();

        // Reset
        v1Handler.Handled = false;

        // 2. Post V2 event
        var contentV2 = new StringContent("{}", Encoding.UTF8, "application/json");
        contentV2.Headers.Add(CloudEventConstants.TypeHeader, "order.v2");
        var res2 = await client.PostAsync("/centra/events/pubsub-test/orders", contentV2);
        res2.StatusCode.ShouldBe(HttpStatusCode.OK);
        v1Handler.Handled.ShouldBeFalse();
        v2Handler.Handled.ShouldBeTrue();
        defaultHandler.Handled.ShouldBeFalse();

        // Reset
        v2Handler.Handled = false;

        // 3. Post unknown event -> routes to default handler
        var contentOther = new StringContent("{}", Encoding.UTF8, "application/json");
        contentOther.Headers.Add(CloudEventConstants.TypeHeader, "order.unknown");
        var res3 = await client.PostAsync("/centra/events/pubsub-test/orders", contentOther);
        res3.StatusCode.ShouldBe(HttpStatusCode.OK);
        v1Handler.Handled.ShouldBeFalse();
        v2Handler.Handled.ShouldBeFalse();
        defaultHandler.Handled.ShouldBeTrue();
    }
}
