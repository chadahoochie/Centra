using System.Net;
using System.Text;
using System.Text.Json;
using Centra.Diagnostics;
using Centra.Events;
using Centra.Invocation;
using Centra.Resilience;
using Centra.Serialization;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Invocation;

public sealed class CentraServiceInvokerTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public void Constructor_NullHttpClient_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new CentraServiceInvoker(null!));
    }

    [Fact]
    public async Task InvokeMethodAsync_Success_SerializesRequestAndDeserializesResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ResponseDto("success")), Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var resolver = Substitute.For<IServiceEndpointResolver>();
        resolver.ResolveEndpointAsync("order-svc", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Uri?>(new Uri("https://order.internal:5000/")));

        var invoker = new CentraServiceInvoker(client, JsonCentraSerializer.Default, resolver);

        // Act
        var result = await invoker.InvokeMethodAsync<RequestDto, ResponseDto>(
            "order-svc",
            "create-order",
            new RequestDto("order-1"));

        // Assert
        result.ShouldNotBeNull();
        result.Status.ShouldBe("success");

        capturedRequest.ShouldNotBeNull();
        capturedRequest.RequestUri.ShouldBe(new Uri("https://order.internal:5000/create-order"));
        capturedRequest.Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task InvokeMethodAsync_PropagatesAmbientContextAndCustomHeaders()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ResponseDto("ok")), Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var invoker = new CentraServiceInvoker(client);

        CentraAmbientContext.CorrelationId = "corr-123";
        CentraAmbientContext.CausationId = "cause-456";
        CentraAmbientContext.TenantId = "tenant-789";

        try
        {
            var options = new ServiceInvocationOptions
            {
                Headers = new Dictionary<string, string> { ["X-Custom-Header"] = "val" }
            };

            await invoker.InvokeMethodAsync<RequestDto, ResponseDto>(
                "order-svc",
                "items",
                new RequestDto("1"),
                "GET",
                options);

            capturedRequest.ShouldNotBeNull();
            capturedRequest.Method.ShouldBe(HttpMethod.Get);
            capturedRequest.Headers.GetValues(CloudEventConstants.CorrelationIdHeader).First().ShouldBe("corr-123");
            capturedRequest.Headers.GetValues(CloudEventConstants.CausationIdHeader).First().ShouldBe("cause-456");
            capturedRequest.Headers.GetValues(CloudEventConstants.TenantIdHeader).First().ShouldBe("tenant-789");
            capturedRequest.Headers.GetValues("X-Custom-Header").First().ShouldBe("val");
        }
        finally
        {
            CentraAmbientContext.Clear();
        }
    }

    [Fact]
    public async Task InvokeMethodAsync_ResilienceEnabled_ExecutesThroughPipeline()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ResponseDto("resilient")), Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var resilienceProvider = Substitute.For<IResiliencePipelineProvider>();
        var pipeline = Substitute.For<IResiliencePipeline>();

        pipeline.ExecuteAsync(Arg.Any<Func<CancellationToken, ValueTask<ReadOnlyMemory<byte>>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var callback = callInfo.Arg<Func<CancellationToken, ValueTask<ReadOnlyMemory<byte>>>>();
                return callback(CancellationToken.None);
            });

        resilienceProvider.GetServiceInvocationPipeline("order-svc").Returns(pipeline);

        var invoker = new CentraServiceInvoker(client, JsonCentraSerializer.Default, null, resilienceProvider);

        var result = await invoker.InvokeMethodAsync<RequestDto, ResponseDto>(
            "order-svc",
            "test-resilience",
            new RequestDto("r1"));

        result.Status.ShouldBe("resilient");
        resilienceProvider.Received(1).GetServiceInvocationPipeline("order-svc");
    }

    [Fact]
    public async Task InvokeMethodAsync_ResilienceDisabledInOptions_BypassesPipeline()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new ResponseDto("bypassed")), Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var resilienceProvider = Substitute.For<IResiliencePipelineProvider>();
        var invoker = new CentraServiceInvoker(client, JsonCentraSerializer.Default, null, resilienceProvider);

        var options = new ServiceInvocationOptions { DisableResilience = true };

        var result = await invoker.InvokeMethodAsync<RequestDto, ResponseDto>(
            "order-svc",
            "test-bypass",
            new RequestDto("r1"),
            null,
            options);

        result.Status.ShouldBe("bypassed");
        resilienceProvider.DidNotReceiveWithAnyArgs().GetServiceInvocationPipeline(default!);
    }

    [Fact]
    public async Task InvokeMethodAsync_HttpError_ThrowsAndRecordsError()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var client = new HttpClient(handler);
        var invoker = new CentraServiceInvoker(client);

        await Should.ThrowAsync<HttpRequestException>(() =>
            invoker.InvokeMethodAsync<RequestDto, ResponseDto>("failing-svc", "error-endpoint", new RequestDto("e1")).AsTask());
    }

    [Fact]
    public async Task InvokeMethodAsync_NullDeserializedResult_ThrowsInvalidOperationException()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null", Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var invoker = new CentraServiceInvoker(client);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            invoker.InvokeMethodAsync<RequestDto, ResponseDto>("order-svc", "null-endpoint", new RequestDto("n1")).AsTask());

        ex.Message.ShouldContain("Failed to deserialize response");
    }

    private sealed record RequestDto(string Id);
    private sealed record ResponseDto(string Status);
}
