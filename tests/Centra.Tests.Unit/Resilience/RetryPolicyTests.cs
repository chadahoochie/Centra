using Centra.Resilience;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Resilience;

public sealed class RetryPolicyTests
{
    [Fact]
    public async Task Should_Execute_Successfully_Without_Retries_When_No_Failure()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "test-retry-success",
            Retry: new RetryPolicyOptions(MaxRetries: 3, BaseDelay: TimeSpan.FromMilliseconds(1), UseJitter: false)));

        var pipeline = registry.GetPipeline("test-retry-success");
        var executionCount = 0;

        // Act
        var result = await pipeline.ExecuteAsync(async ct =>
        {
            await Task.Yield();
            executionCount++;
            return 42;
        });

        // Assert
        result.ShouldBe(42);
        executionCount.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Retry_And_Succeed_When_Transient_Failures_Resolve()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "test-transient-retry",
            Retry: new RetryPolicyOptions(
                MaxRetries: 3,
                BackoffType: CentraBackoffType.Constant,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var pipeline = registry.GetPipeline("test-transient-retry");
        var attempts = 0;

        // Act
        var result = await pipeline.ExecuteAsync(async ct =>
        {
            await Task.Yield();
            attempts++;
            if (attempts < 3)
            {
                throw new InvalidOperationException($"Transient failure attempt {attempts}");
            }
            return "recovered";
        });

        // Assert
        result.ShouldBe("recovered");
        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task Should_Throw_When_Max_Retries_Exceeded()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        const int maxRetries = 2;
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "test-retry-exhausted",
            Retry: new RetryPolicyOptions(
                MaxRetries: maxRetries,
                BackoffType: CentraBackoffType.Constant,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var pipeline = registry.GetPipeline("test-retry-exhausted");
        var attempts = 0;

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                await Task.Yield();
                attempts++;
                throw new InvalidOperationException("Persistent error");
            });
        });

        ex.Message.ShouldBe("Persistent error");
        attempts.ShouldBe(maxRetries + 1);
    }

    [Theory]
    [InlineData(CentraBackoffType.Constant)]
    [InlineData(CentraBackoffType.Linear)]
    [InlineData(CentraBackoffType.Exponential)]
    public async Task Should_Support_Configured_Backoff_Types(CentraBackoffType backoffType)
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        var policyName = $"backoff-{backoffType}";
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: policyName,
            Retry: new RetryPolicyOptions(
                MaxRetries: 2,
                BackoffType: backoffType,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var pipeline = registry.GetPipeline(policyName);
        var attempts = 0;

        // Act
        var result = await pipeline.ExecuteAsync(async ct =>
        {
            await Task.Yield();
            attempts++;
            if (attempts < 2)
            {
                throw new TimeoutException("Temporary timeout");
            }
            return "done";
        });

        // Assert
        result.ShouldBe("done");
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Should_Respect_CancellationToken_During_Retries()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "test-cancellation",
            Retry: new RetryPolicyOptions(
                MaxRetries: 10,
                BaseDelay: TimeSpan.FromMilliseconds(50),
                UseJitter: false)));

        var pipeline = registry.GetPipeline("test-cancellation");
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                attempts++;
                if (attempts == 2)
                {
                    cts.Cancel();
                }
                await Task.Yield();
                throw new HttpRequestException("Simulated error");
            }, cts.Token);
        });

        attempts.ShouldBe(2);
    }
}
