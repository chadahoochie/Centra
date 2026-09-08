using Centra.Resilience;
using Polly.Timeout;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Resilience;

public sealed class TimeoutPolicyTests
{
    [Fact]
    public async Task Should_Complete_Successfully_When_Action_Finishes_Before_Timeout()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "test-timeout-success",
            Timeout: new TimeoutPolicyOptions(TimeSpan.FromSeconds(2))));

        var pipeline = registry.GetPipeline("test-timeout-success");

        // Act
        var result = await pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(10, ct);
            return "ok";
        });

        // Assert
        result.ShouldBe("ok");
    }

    [Fact]
    public async Task Should_Throw_TimeoutRejectedException_When_Action_Exceeds_Timeout()
    {
        // Arrange
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var registry = new PollyResiliencePipelineRegistry(timeProvider: fakeTime);
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "test-timeout-exceeded",
            Timeout: new TimeoutPolicyOptions(TimeSpan.FromMilliseconds(50))));

        var pipeline = registry.GetPipeline("test-timeout-exceeded");

        // Act & Assert
        await Should.ThrowAsync<TimeoutRejectedException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                var delayTask = Task.Delay(TimeSpan.FromSeconds(10), fakeTime, ct);
                fakeTime.Advance(TimeSpan.FromMilliseconds(100));
                await delayTask;
                return "never";
            });
        });
    }

    [Fact]
    public async Task Should_Signal_Cancellation_Token_When_Timeout_Occurs()
    {
        // Arrange
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var registry = new PollyResiliencePipelineRegistry(timeProvider: fakeTime);
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "test-timeout-token",
            Timeout: new TimeoutPolicyOptions(TimeSpan.FromMilliseconds(50))));

        var pipeline = registry.GetPipeline("test-timeout-token");
        var tokenCancelled = false;

        // Act & Assert
        await Should.ThrowAsync<TimeoutRejectedException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                try
                {
                    var delayTask = Task.Delay(TimeSpan.FromSeconds(10), fakeTime, ct);
                    fakeTime.Advance(TimeSpan.FromMilliseconds(100));
                    await delayTask;
                }
                catch (OperationCanceledException)
                {
                    tokenCancelled = ct.IsCancellationRequested;
                    throw;
                }
            });
        });

        tokenCancelled.ShouldBeTrue();
    }
}
