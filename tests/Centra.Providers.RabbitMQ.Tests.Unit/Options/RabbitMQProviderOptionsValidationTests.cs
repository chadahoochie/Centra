using Centra.Providers.RabbitMQ.Extensions;
using Centra.Providers.RabbitMQ.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.Options;

/// <summary>
/// A shutdown drain budget that is neither positive nor <see cref="Timeout.InfiniteTimeSpan"/> would
/// silently skip the drain entirely, so registration must reject it instead.
/// </summary>
public sealed class RabbitMQProviderOptionsValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5000)]
    public void Resolving_Options_Should_Fail_When_The_Total_Shutdown_Drain_Timeout_Is_Not_Positive(int milliseconds)
    {
        var services = new ServiceCollection();
        services.AddCentraRabbitMQ(o => o.TotalShutdownDrainTimeout = TimeSpan.FromMilliseconds(milliseconds));
        using var provider = services.BuildServiceProvider();

        var ex = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RabbitMQProviderOptions>>().Value);

        ex.Message.ShouldContain(nameof(RabbitMQProviderOptions.TotalShutdownDrainTimeout));
    }

    [Fact]
    public void Resolving_Options_Should_Accept_An_Infinite_Total_Shutdown_Drain_Timeout()
    {
        var services = new ServiceCollection();
        services.AddCentraRabbitMQ(o => o.TotalShutdownDrainTimeout = Timeout.InfiniteTimeSpan);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<RabbitMQProviderOptions>>().Value
            .TotalShutdownDrainTimeout.ShouldBe(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void Resolving_Options_Should_Accept_The_Default_Total_Shutdown_Drain_Timeout()
    {
        var services = new ServiceCollection();
        services.AddCentraRabbitMQ();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<RabbitMQProviderOptions>>().Value
            .TotalShutdownDrainTimeout.ShouldBe(TimeSpan.FromSeconds(10));
    }
}
