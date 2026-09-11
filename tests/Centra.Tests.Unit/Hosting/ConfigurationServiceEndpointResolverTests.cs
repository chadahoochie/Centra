using Centra.Hosting.Invocation;
using Centra.Invocation;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class ConfigurationServiceEndpointResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveEndpointAsync_NullOrWhiteSpaceAppId_ThrowsArgumentException(string? appId)
    {
        var resolver = new ConfigurationServiceEndpointResolver(null);
        await Should.ThrowAsync<ArgumentException>(() =>
            resolver.ResolveEndpointAsync(appId!).AsTask());
    }

    [Fact]
    public async Task ResolveEndpointAsync_ResolvesFromCentraServicesAddress_WithoutTrailingSlash()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Centra:Services:order-service:Address"] = "https://order-svc.internal:8080"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var resolver = new ConfigurationServiceEndpointResolver(config);

        var uri = await resolver.ResolveEndpointAsync("order-service");

        uri.ShouldNotBeNull();
        uri.ToString().ShouldBe("https://order-svc.internal:8080/");
    }

    [Fact]
    public async Task ResolveEndpointAsync_ResolvesFromCentraServicesAddress_WithTrailingSlash()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Centra:Services:order-service:Address"] = "https://order-svc.internal:8080/"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var resolver = new ConfigurationServiceEndpointResolver(config);

        var uri = await resolver.ResolveEndpointAsync("order-service");

        uri.ShouldNotBeNull();
        uri.ToString().ShouldBe("https://order-svc.internal:8080/");
    }

    [Fact]
    public async Task ResolveEndpointAsync_ResolvesFromAspireHttpKey()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["services:payment-service:http:0"] = "http://payment.aspire:5000"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var resolver = new ConfigurationServiceEndpointResolver(config);

        var uri = await resolver.ResolveEndpointAsync("payment-service");

        uri.ShouldNotBeNull();
        uri.ToString().ShouldBe("http://payment.aspire:5000/");
    }

    [Fact]
    public async Task ResolveEndpointAsync_ResolvesFromAspireHttpsKey()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["services:inventory-service:https:0"] = "https://inventory.aspire:5001"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var resolver = new ConfigurationServiceEndpointResolver(config);

        var uri = await resolver.ResolveEndpointAsync("inventory-service");

        uri.ShouldNotBeNull();
        uri.ToString().ShouldBe("https://inventory.aspire:5001/");
    }

    [Fact]
    public async Task ResolveEndpointAsync_ConfigurationNull_DelegatesToFallback()
    {
        var fallback = Substitute.For<IServiceEndpointResolver>();
        var expectedUri = new Uri("https://fallback.domain/");
        fallback.ResolveEndpointAsync("custom-app", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Uri?>(expectedUri));

        var resolver = new ConfigurationServiceEndpointResolver(null, fallback);

        var uri = await resolver.ResolveEndpointAsync("custom-app");

        uri.ShouldBe(expectedUri);
        await fallback.Received(1).ResolveEndpointAsync("custom-app", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveEndpointAsync_NoMatchingConfigKeys_DelegatesToDefaultFallback()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var resolver = new ConfigurationServiceEndpointResolver(emptyConfig);

        var uri = await resolver.ResolveEndpointAsync("customer-service");

        uri.ShouldNotBeNull();
        uri.ToString().ShouldBe("http://customer-service/");
    }
}
