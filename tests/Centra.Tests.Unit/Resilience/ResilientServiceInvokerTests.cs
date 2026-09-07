using System.Net;
using System.Text;
using Centra.Invocation;
using Centra.Resilience;
using Centra.Tests.Unit.Sync;
using Polly.CircuitBreaker;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Resilience;

public sealed class ResilientServiceInvokerTests
{
    [Fact]
    public async Task Should_Retry_Transient_503_Http_Failures_And_Succeed()
    {
        // Arrange
        var attemptCount = 0;
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}", Encoding.UTF8, "application/json")
            });
        });

        var httpClient = new HttpClient(handler);
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "invocation:inventory-service",
            Retry: new RetryPolicyOptions(
                MaxRetries: 3,
                BackoffType: CentraBackoffType.Constant,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var invoker = new CentraServiceInvoker(httpClient, resilienceProvider: registry);

        // Act
        var result = await invoker.InvokeMethodRawAsync(
            "inventory-service",
            "api/v1/stock",
            Encoding.UTF8.GetBytes("{\"item\":\"apple\"}"),
            httpVerb: "POST");

        // Assert
        attemptCount.ShouldBe(2);
        Encoding.UTF8.GetString(result.Span).ShouldContain("ok");
    }

    [Fact]
    public async Task Should_Throw_When_Retry_Attempts_Exhausted()
    {
        // Arrange
        var attemptCount = 0;
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            attemptCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        var httpClient = new HttpClient(handler);
        var registry = new PollyResiliencePipelineRegistry();
        const int maxRetries = 2;
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "invocation:payment-service",
            Retry: new RetryPolicyOptions(
                MaxRetries: maxRetries,
                BackoffType: CentraBackoffType.Constant,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var invoker = new CentraServiceInvoker(httpClient, resilienceProvider: registry);

        // Act & Assert
        await Should.ThrowAsync<HttpRequestException>(async () =>
        {
            await invoker.InvokeMethodRawAsync("payment-service", "charge", ReadOnlyMemory<byte>.Empty);
        });

        attemptCount.ShouldBe(maxRetries + 1);
    }

    [Fact]
    public async Task Should_Bypass_Resilience_When_DisableResilience_Is_True()
    {
        // Arrange
        var attemptCount = 0;
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            attemptCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        var httpClient = new HttpClient(handler);
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "invocation:orders-service",
            Retry: new RetryPolicyOptions(
                MaxRetries: 3,
                BackoffType: CentraBackoffType.Constant,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var invoker = new CentraServiceInvoker(httpClient, resilienceProvider: registry);

        // Act & Assert
        await Should.ThrowAsync<HttpRequestException>(async () =>
        {
            await invoker.InvokeMethodRawAsync(
                "orders-service",
                "orders",
                ReadOnlyMemory<byte>.Empty,
                options: new ServiceInvocationOptions { DisableResilience = true });
        });

        attemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Trip_Circuit_Breaker_When_Service_Repeatedly_Fails()
    {
        // Arrange
        var callCount = 0;
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        });

        var httpClient = new HttpClient(handler);
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "invocation:failing-service",
            CircuitBreaker: new CircuitBreakerPolicyOptions(
                FailureRatio: 0.5,
                SamplingDuration: TimeSpan.FromSeconds(10),
                MinimumThroughput: 2,
                BreakDuration: TimeSpan.FromSeconds(5))));

        var invoker = new CentraServiceInvoker(httpClient, resilienceProvider: registry);

        // Act - Cause 2 failures to trip circuit
        for (var i = 0; i < 2; i++)
        {
            await Should.ThrowAsync<HttpRequestException>(async () =>
            {
                await invoker.InvokeMethodRawAsync("failing-service", "endpoint", ReadOnlyMemory<byte>.Empty);
            });
        }

        callCount.ShouldBe(2);

        // Next call should throw BrokenCircuitException immediately without calling HTTP client
        await Should.ThrowAsync<BrokenCircuitException>(async () =>
        {
            await invoker.InvokeMethodRawAsync("failing-service", "endpoint", ReadOnlyMemory<byte>.Empty);
        });

        callCount.ShouldBe(2);
    }
}
