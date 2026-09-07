using System.Net;
using System.Text;
using Centra.Bindings;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Bindings;

public sealed class HttpWebhookBindingDriverTests
{
    [Fact]
    public void Constructor_NullHttpClient_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new HttpWebhookBindingDriver(null!));
    }

    [Fact]
    public async Task InvokeAsync_PostRequest_SendsPayloadAndReturnsResponseBody()
    {
        var handler = new MockHttpMessageHandler(async request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.ToString().ShouldBe("https://webhook.site/test");
            request.Headers.Contains("X-Custom-Header").ShouldBeTrue();

            var body = await request.Content!.ReadAsStringAsync();
            body.ShouldBe("{\"orderId\":\"123\"}");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"received\"}", Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(handler);
        var driver = new HttpWebhookBindingDriver(client);

        var metadata = new Dictionary<string, string>
        {
            ["url"] = "https://webhook.site/test",
            ["header:X-Custom-Header"] = "TestValue"
        };

        var request = new BindingRequest(Encoding.UTF8.GetBytes("{\"orderId\":\"123\"}"), metadata, "POST");

        var response = await driver.InvokeAsync("webhook", request);

        Encoding.UTF8.GetString(response.Data.Span).ShouldBe("{\"status\":\"received\"}");
        response.Metadata.ShouldNotBeNull();
        response.Metadata["status_code"].ShouldBe("200");
    }

    [Fact]
    public async Task InvokeAsync_MissingUrl_ThrowsInvalidOperationException()
    {
        var client = new HttpClient(new MockHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var driver = new HttpWebhookBindingDriver(client);

        var request = new BindingRequest(ReadOnlyMemory<byte>.Empty);

        await Should.ThrowAsync<InvalidOperationException>(async () => await driver.InvokeAsync("webhook", request));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("GET")]
    public async Task InvokeAsync_CustomHttpMethods_DispatchesCorrectMethod(string method)
    {
        HttpMethod? capturedMethod = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedMethod = req.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var client = new HttpClient(handler);
        var driver = new HttpWebhookBindingDriver(client);

        var metadata = new Dictionary<string, string> { ["url"] = "https://api.example.com/item" };
        var request = new BindingRequest(ReadOnlyMemory<byte>.Empty, metadata, method);

        await driver.InvokeAsync("http-binding", request);

        capturedMethod.ShouldNotBeNull();
        capturedMethod.Method.ShouldBe(method);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task InvokeAsync_InvalidBindingName_ThrowsArgumentException(string invalid)
    {
        var client = new HttpClient(new MockHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var driver = new HttpWebhookBindingDriver(client);

        await Should.ThrowAsync<ArgumentException>(async () =>
            await driver.InvokeAsync(invalid, new BindingRequest(ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public async Task InvokeAsync_CustomContentTypeAndResponseHeaders_SetsAndReturnsProperHeaders()
    {
        var handler = new MockHttpMessageHandler(async req =>
        {
            req.Content.ShouldNotBeNull();
            req.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");

            var res = new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("ok")
            };
            res.Headers.Add("X-Server-Id", "srv-1");
            return res;
        });

        var client = new HttpClient(handler);
        var driver = new HttpWebhookBindingDriver(client);

        var metadata = new Dictionary<string, string>
        {
            ["url"] = "https://test.local",
            ["content-type"] = "text/plain"
        };
        var request = new BindingRequest(Encoding.UTF8.GetBytes("hello"), metadata, "POST");

        var response = await driver.InvokeAsync("webhook", request);

        response.Metadata.ShouldNotBeNull();
        response.Metadata["status_code"].ShouldBe("202");
        response.Metadata["header:X-Server-Id"].ShouldBe("srv-1");
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handlerFunc(request);
        }
    }
}
