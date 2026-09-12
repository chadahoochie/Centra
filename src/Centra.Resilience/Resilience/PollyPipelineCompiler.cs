using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Centra.Resilience;

internal static class PollyPipelineCompiler
{
    internal static IResiliencePipeline Compile(
        CentraResiliencePolicyDefinition definition,
        TimeProvider timeProvider,
        ILoggerFactory? loggerFactory)
    {
        var builder = new ResiliencePipelineBuilder
        {
            TimeProvider = timeProvider
        };

        var logger = loggerFactory?.CreateLogger("Centra.Resilience");
        var listener = new ResilienceTelemetryListener(definition.PolicyName, logger);

        // 1. Outer: Timeout
        if (definition.Timeout is not null && definition.Timeout.Timeout > TimeSpan.Zero)
        {
            builder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = definition.Timeout.Timeout,
                OnTimeout = args => listener.OnTimeout(args)
            });
        }

        // 2. Concurrency Limiter (Bulkhead)
        if (definition.Bulkhead is not null)
        {
            builder.AddConcurrencyLimiter(
                definition.Bulkhead.MaxParallelism,
                definition.Bulkhead.MaxQueuedActions);
        }

        // 3. Rate Limiter
        if (definition.RateLimiter is not null)
        {
            var window = definition.RateLimiter.Window > TimeSpan.Zero
                ? definition.RateLimiter.Window
                : TimeSpan.FromSeconds(1);

            builder.AddRateLimiter(new System.Threading.RateLimiting.SlidingWindowRateLimiter(
                new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
                {
                    PermitLimit = definition.RateLimiter.PermitLimit,
                    QueueLimit = definition.RateLimiter.QueueLimit,
                    Window = window,
                    SegmentsPerWindow = 4
                }));
        }

        // 4. Circuit Breaker
        if (definition.CircuitBreaker is not null)
        {
            var sampling = ClampDuration(
                definition.CircuitBreaker.SamplingDuration,
                min: TimeSpan.FromMilliseconds(500),
                fallback: TimeSpan.FromSeconds(10));

            var breakDur = ClampDuration(
                definition.CircuitBreaker.BreakDuration,
                min: TimeSpan.FromMilliseconds(500),
                fallback: TimeSpan.FromSeconds(5));

            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = definition.CircuitBreaker.FailureRatio,
                SamplingDuration = sampling,
                MinimumThroughput = definition.CircuitBreaker.MinimumThroughput,
                BreakDuration = breakDur,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                OnOpened = args => listener.OnCircuitOpened(args),
                OnClosed = args => listener.OnCircuitClosed(args),
                OnHalfOpened = args => listener.OnCircuitHalfOpened(args)
            });
        }

        // 5. Inner: Retry
        if (definition.Retry is not null && definition.Retry.MaxRetries > 0)
        {
            var baseDelay = definition.Retry.BaseDelay > TimeSpan.Zero
                ? definition.Retry.BaseDelay
                : TimeSpan.FromMilliseconds(100);

            var maxDelay = definition.Retry.MaxDelay > TimeSpan.Zero
                ? definition.Retry.MaxDelay
                : TimeSpan.FromSeconds(2);

            var backoff = definition.Retry.BackoffType switch
            {
                CentraBackoffType.Constant => DelayBackoffType.Constant,
                CentraBackoffType.Linear => DelayBackoffType.Linear,
                _ => DelayBackoffType.Exponential
            };

            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = definition.Retry.MaxRetries,
                BackoffType = backoff,
                Delay = baseDelay,
                MaxDelay = maxDelay,
                UseJitter = definition.Retry.UseJitter,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                OnRetry = args => listener.OnRetry(args)
            });
        }

        var pipeline = builder.Build();
        return new PollyResiliencePipeline(pipeline);
    }

    internal static CentraResiliencePolicyDefinition CreateDefaultGenericPolicy(string name) =>
        new(
            PolicyName: name,
            Retry: new RetryPolicyOptions(
                MaxRetries: 3,
                BackoffType: CentraBackoffType.Exponential,
                BaseDelay: TimeSpan.FromMilliseconds(100),
                MaxDelay: TimeSpan.FromSeconds(2),
                UseJitter: true));

    internal static TimeSpan ClampDuration(TimeSpan configured, TimeSpan min, TimeSpan fallback)
    {
        if (configured <= TimeSpan.Zero)
        {
            return fallback;
        }

        return configured < min ? min : configured;
    }
}
