using System.Net;
using System.Text;
using Centra.Bindings;
using Centra.Hosting.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraEndpointRouteBuilderBindingsTests
{
    [Fact]
    public async Task MapCentraEndpoints_InputBinding_RoutesToHandlerAndReturnsResponse()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddCentraBindings();
                    services.AddCentraInputBindingHandler<EchoTriggerHandler>("echo-in");
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapCentraEndpoints();
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        var content = new StringContent("Hello from webhook", Encoding.UTF8, "text/plain");
        var response = await client.PostAsync("/centra/bindings/echo-in", content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var responseBody = await response.Content.ReadAsStringAsync();
        responseBody.ShouldBe("ECHO: Hello from webhook");
    }

    [Fact]
    public async Task MapCentraEndpoints_UnregisteredBinding_ReturnsNotFound()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddCentraBindings();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapCentraEndpoints();
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var response = await client.PostAsync("/centra/bindings/unknown", new StringContent("data"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapCentraEndpoints_InputBinding_EmptyResponseData_ReturnsOk()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddCentraBindings();
                    services.AddCentraInputBindingHandler<EmptyResponseTriggerHandler>("empty-in");
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapCentraEndpoints());
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var response = await client.PostAsync("/centra/bindings/empty-in", new StringContent("data"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapCentraEndpoints_InputBinding_CustomHeadersAndContentType_SetsResponseHeaders()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddCentraBindings();
                    services.AddCentraInputBindingHandler<CustomHeaderTriggerHandler>("custom-in");
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapCentraEndpoints());
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var response = await client.PostAsync("/centra/bindings/custom-in", new StringContent("data"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        response.Headers.Contains("X-Trigger-Source").ShouldBeTrue();
    }

    [Fact]
    public async Task MapCentraEndpoints_InputBinding_WhenHandlerThrows_Returns500()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddCentraBindings();
                    services.AddCentraInputBindingHandler<ThrowingTriggerHandler>("throw-in");
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapCentraEndpoints());
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var response = await client.PostAsync("/centra/bindings/throw-in", new StringContent("data"));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    private sealed class EmptyResponseTriggerHandler : IBindingTriggerHandler
    {
        public ValueTask<BindingResponse> HandleTriggerAsync(BindingData data, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new BindingResponse(ReadOnlyMemory<byte>.Empty));
        }
    }

    private sealed class CustomHeaderTriggerHandler : IBindingTriggerHandler
    {
        public ValueTask<BindingResponse> HandleTriggerAsync(BindingData data, CancellationToken cancellationToken = default)
        {
            var meta = new Dictionary<string, string>
            {
                ["content-type"] = "application/json",
                ["header:X-Trigger-Source"] = "custom-trigger"
            };
            return ValueTask.FromResult(new BindingResponse(Encoding.UTF8.GetBytes("{}"), meta));
        }
    }

    private sealed class ThrowingTriggerHandler : IBindingTriggerHandler
    {
        public ValueTask<BindingResponse> HandleTriggerAsync(BindingData data, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Trigger failed");
        }
    }

    private sealed class EchoTriggerHandler : IBindingTriggerHandler
    {
        public ValueTask<BindingResponse> HandleTriggerAsync(BindingData data, CancellationToken cancellationToken = default)
        {
            var text = Encoding.UTF8.GetString(data.Data.Span);
            var result = Encoding.UTF8.GetBytes($"ECHO: {text}");
            return ValueTask.FromResult(new BindingResponse(result));
        }
    }
}
