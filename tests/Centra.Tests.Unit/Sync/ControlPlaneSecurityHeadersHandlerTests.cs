using System.Net;
using Centra;
using Centra.Sync;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Sync;

public sealed class ControlPlaneSecurityHeadersHandlerTests
{
    [Fact]
    public async Task Should_Add_Basic_Cluster_Token_Header_When_Hmac_Disabled()
    {
        var options = new CentraControlPlaneOptions
        {
            ClusterId = "my-cluster",
            ClusterToken = "token-12345",
            UseHmacAuthentication = false
        };

        var selector = new ControlPlaneEndpointSelector(new[] { "http://cp-1:8080" });
        var handler = new ControlPlaneSecurityHeadersHandler(options, selector);

        var innerHandler = new TestHttpMessageHandler((req, ct) =>
        {
            req.Headers.GetValues("X-Centra-Cluster-Id").First().ShouldBe("my-cluster");
            req.Headers.GetValues("X-Centra-Cluster-Token").First().ShouldBe("token-12345");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        handler.InnerHandler = innerHandler;
        var client = new HttpClient(handler);

        var response = await client.GetAsync("http://cp-1:8080/api/v1/topology");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_Add_Hmac_Headers_When_Hmac_Enabled()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = new CentraControlPlaneOptions
        {
            ClusterId = "cluster-secure",
            ClusterToken = "hmac-secret-key-32-bytes-long!!",
            UseHmacAuthentication = true
        };

        var selector = new ControlPlaneEndpointSelector(new[] { "http://cp-1:8080" });
        var handler = new ControlPlaneSecurityHeadersHandler(options, selector, timeProvider);

        var innerHandler = new TestHttpMessageHandler((req, ct) =>
        {
            req.Headers.GetValues("X-Centra-Cluster-Id").First().ShouldBe("cluster-secure");
            req.Headers.Contains("X-Centra-Timestamp").ShouldBeTrue();
            req.Headers.Contains("X-Centra-Nonce").ShouldBeTrue();
            req.Headers.Contains("X-Centra-Signature").ShouldBeTrue();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        handler.InnerHandler = innerHandler;
        var client = new HttpClient(handler);

        var response = await client.GetAsync("http://cp-1:8080/api/v1/topology");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_Update_Leader_When_Receiving_307_Or_Leader_Header()
    {
        var options = new CentraControlPlaneOptions
        {
            ClusterId = "cluster-1",
            Endpoints = new[] { "http://cp-1:8080", "http://cp-2:8081" }
        };

        var selector = new ControlPlaneEndpointSelector(options.Endpoints);
        var handler = new ControlPlaneSecurityHeadersHandler(options, selector);

        var innerHandler = new TestHttpMessageHandler((req, ct) =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            res.Headers.Add("Location", "http://cp-2:8081/api/v1/topology");
            res.Headers.Add("X-Centra-Leader", "http://cp-2:8081");
            return Task.FromResult(res);
        });

        handler.InnerHandler = innerHandler;
        var client = new HttpClient(handler);

        await client.GetAsync("http://cp-1:8080/api/v1/topology");

        selector.GetCurrentEndpoint().ShouldBe("http://cp-2:8081");
    }
}
