using Centra.Resilience;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Resilience;

public sealed class PollyResiliencePipelineRegistryTests
{
    [Fact]
    public void Should_Register_And_Retrieve_Policy_Definition()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        var definition = new CentraResiliencePolicyDefinition(
            PolicyName: "custom-policy",
            Retry: new RetryPolicyOptions(MaxRetries: 5, BackoffType: CentraBackoffType.Linear),
            Timeout: new TimeoutPolicyOptions(TimeSpan.FromSeconds(2)));

        // Act
        registry.RegisterPolicy(definition);
        var retrieved = registry.GetPolicy("custom-policy");
        var all = registry.GetAllPolicies();

        // Assert
        retrieved.ShouldNotBeNull();
        retrieved.PolicyName.ShouldBe("custom-policy");
        retrieved.Retry.ShouldNotBeNull();
        retrieved.Retry.MaxRetries.ShouldBe(5);
        retrieved.Retry.BackoffType.ShouldBe(CentraBackoffType.Linear);
        all.Count.ShouldBe(1);
        all.ShouldContain(p => p.PolicyName == "custom-policy");
    }

    [Fact]
    public void Should_Return_Null_When_Policy_Not_Found()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();

        // Act
        var retrieved = registry.GetPolicy("non-existent");

        // Assert
        retrieved.ShouldBeNull();
    }

    [Fact]
    public void Should_Remove_Policy_And_Invalidate_Cache()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        var definition = new CentraResiliencePolicyDefinition(
            PolicyName: "temp-policy",
            Retry: new RetryPolicyOptions(MaxRetries: 2));

        registry.RegisterPolicy(definition);
        var pipeline = registry.GetPipeline("temp-policy");
        pipeline.ShouldNotBeNull();

        // Act
        var removed = registry.RemovePolicy("temp-policy");
        var secondRemoval = registry.RemovePolicy("temp-policy");

        // Assert
        removed.ShouldBeTrue();
        secondRemoval.ShouldBeFalse();
        registry.GetPolicy("temp-policy").ShouldBeNull();
    }

    [Fact]
    public void Should_Provide_Default_Service_Invocation_Pipeline()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();

        // Act
        var pipeline = registry.GetServiceInvocationPipeline("payment-service");

        // Assert
        pipeline.ShouldNotBeNull();
    }

    [Fact]
    public void Should_Provide_Default_State_Store_Pipeline()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();

        // Act
        var pipeline = registry.GetStateStorePipeline("orders-table");

        // Assert
        pipeline.ShouldNotBeNull();
    }

    [Fact]
    public void Should_Provide_Default_PubSub_Pipeline()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();

        // Act
        var pipeline = registry.GetPubSubPipeline("order-events");

        // Assert
        pipeline.ShouldNotBeNull();
    }

    [Fact]
    public void Should_Honor_Global_Default_Invocation_Policy()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        var globalDefault = new CentraResiliencePolicyDefinition(
            PolicyName: "default-invocation",
            Retry: new RetryPolicyOptions(MaxRetries: 7));
        registry.RegisterPolicy(globalDefault);

        // Act
        var pipeline = registry.GetServiceInvocationPipeline("inventory-service");

        // Assert
        pipeline.ShouldNotBeNull();
    }

    [Fact]
    public void Should_Prioritize_Target_Specific_Policy_Over_Global_Default()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "default-invocation",
            Retry: new RetryPolicyOptions(MaxRetries: 2)));

        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "invocation:checkout-service",
            Retry: new RetryPolicyOptions(MaxRetries: 10)));

        // Act
        var pipeline = registry.GetServiceInvocationPipeline("checkout-service");

        // Assert
        pipeline.ShouldNotBeNull();
    }

    [Fact]
    public void Should_Recompile_Pipeline_When_Policy_Is_Updated()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        var initial = new CentraResiliencePolicyDefinition(
            PolicyName: "dynamic-policy",
            Retry: new RetryPolicyOptions(MaxRetries: 1));

        registry.RegisterPolicy(initial);
        var pipeline1 = registry.GetPipeline("dynamic-policy");

        // Act - update policy
        var updated = new CentraResiliencePolicyDefinition(
            PolicyName: "dynamic-policy",
            Retry: new RetryPolicyOptions(MaxRetries: 5));
        registry.RegisterPolicy(updated);
        var pipeline2 = registry.GetPipeline("dynamic-policy");

        // Assert - pipeline instance should be recompiled/replaced
        pipeline2.ShouldNotBeSameAs(pipeline1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Throw_On_Invalid_PolicyName_In_GetPipeline(string? invalidName)
    {
        var registry = new PollyResiliencePipelineRegistry();
        Should.Throw<ArgumentException>(() => registry.GetPipeline(invalidName!));
    }

    [Fact]
    public void Should_Throw_On_Null_Policy_Definition()
    {
        var registry = new PollyResiliencePipelineRegistry();
        Should.Throw<ArgumentNullException>(() => registry.RegisterPolicy(null!));
    }

    [Fact]
    public void Should_Invalidate_Derived_Invocation_Pipelines_When_Default_Invocation_Policy_Changes()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "default-invocation",
            Retry: new RetryPolicyOptions(MaxRetries: 2)));

        var p1 = registry.GetServiceInvocationPipeline("orders-service");

        // Act - Update global default
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "default-invocation",
            Retry: new RetryPolicyOptions(MaxRetries: 7)));

        var p2 = registry.GetServiceInvocationPipeline("orders-service");

        // Assert - Derived pipeline must be recompiled with new settings
        p2.ShouldNotBeSameAs(p1);
    }

    [Fact]
    public async Task Should_Enforce_RateLimiter_Window_And_Reject_Excessive_Requests()
    {
        // Arrange
        var registry = new PollyResiliencePipelineRegistry();
        registry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "rate-limited-policy",
            RateLimiter: new RateLimiterPolicyOptions(
                PermitLimit: 2,
                QueueLimit: 0,
                Window: TimeSpan.FromSeconds(10))));

        var pipeline = registry.GetPipeline("rate-limited-policy");

        // Act & Assert
        // First 2 executions within window succeed
        var r1 = await pipeline.ExecuteAsync(ct => ValueTask.FromResult(1));
        var r2 = await pipeline.ExecuteAsync(ct => ValueTask.FromResult(2));
        r1.ShouldBe(1);
        r2.ShouldBe(2);

        // 3rd sequential execution exceeds window permit limit even with zero active concurrency
        await Should.ThrowAsync<Polly.RateLimiting.RateLimiterRejectedException>(async () =>
        {
            await pipeline.ExecuteAsync(ct => ValueTask.FromResult(3));
        });
    }
}
