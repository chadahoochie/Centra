using Centra.Resilience;
using Polly.CircuitBreaker;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Resilience;

public sealed class CircuitBreakerPolicyTests
{
    [Fact]
    public async Task Should_Open_Circuit_When_Failure_Threshold_Exceeded()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "test-circuit-trip",
            CircuitBreaker: new CircuitBreakerPolicyOptions(
                FailureRatio: 0.5,
                SamplingDuration: TimeSpan.FromSeconds(10),
                MinimumThroughput: 2,
                BreakDuration: TimeSpan.FromMilliseconds(500))));

        var pipeline = registry.GetPipeline("test-circuit-trip");

        // Act - Cause 2 consecutive failures (meets minimum throughput 2, 100% failure ratio >= 0.5)
        for (var i = 0; i < 2; i++)
        {
            await Should.ThrowAsync<InvalidOperationException>(async () =>
            {
                await pipeline.ExecuteAsync(async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Downstream error");
                });
            });
        }

        // Assert - Next invocation should immediately throw BrokenCircuitException without executing inner action
        var actionExecuted = false;
        var ex = await Should.ThrowAsync<BrokenCircuitException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                await Task.Yield();
                actionExecuted = true;
                return 1;
            });
        });

        actionExecuted.ShouldBeFalse();
        ex.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_Recover_To_Closed_State_After_Break_Duration_And_Successful_Probe()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "test-circuit-recovery",
            CircuitBreaker: new CircuitBreakerPolicyOptions(
                FailureRatio: 0.5,
                SamplingDuration: TimeSpan.FromSeconds(5),
                MinimumThroughput: 2,
                BreakDuration: TimeSpan.FromMilliseconds(500))));

        var pipeline = registry.GetPipeline("test-circuit-recovery");

        // 1. Trip the circuit
        for (var i = 0; i < 2; i++)
        {
            await Should.ThrowAsync<InvalidOperationException>(async () =>
            {
                await pipeline.ExecuteAsync(async ct =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("Fail");
                });
            });
        }

        // Verify it is open
        await Should.ThrowAsync<BrokenCircuitException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                await Task.Yield();
                return true;
            });
        });

        // 2. Wait for break duration to expire so circuit transitions to HalfOpen
        await Task.Delay(600);

        // 3. Next execution is the probe request - succeed
        var probeResult = await pipeline.ExecuteAsync(async ct =>
        {
            await Task.Yield();
            return "recovered";
        });

        probeResult.ShouldBe("recovered");

        // 4. Circuit is now closed again; subsequent requests should succeed normally
        var normalResult = await pipeline.ExecuteAsync(async ct =>
        {
            await Task.Yield();
            return "normal";
        });

        normalResult.ShouldBe("normal");
    }
}
