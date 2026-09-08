using System.Collections.Concurrent;
using Centra.Diagnostics;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;

namespace Centra.Resilience;

/// <summary>
/// Thread-safe registry and provider of compiled Polly v8 resilience pipelines.
/// </summary>
public sealed class PollyResiliencePipelineRegistry : IResiliencePipelineProvider, IResiliencePolicyRegistry
{
    private readonly ConcurrentDictionary<string, CentraResiliencePolicyDefinition> _policies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IResiliencePipeline> _pipelineCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILoggerFactory? _loggerFactory;
    private readonly TimeProvider _timeProvider;

    public PollyResiliencePipelineRegistry(
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null)
    {
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public void RegisterPolicy(CentraResiliencePolicyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _policies[definition.PolicyName] = definition;
        InvalidateCache(definition.PolicyName);
    }

    /// <inheritdoc />
    public bool RemovePolicy(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        var removed = _policies.TryRemove(policyName, out _);
        InvalidateCache(policyName);
        return removed;
    }

    private void InvalidateCache(string policyName)
    {
        _pipelineCache.TryRemove(policyName, out _);

        if (policyName.StartsWith("default-", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = policyName["default-".Length..] + ":";
            foreach (var key in _pipelineCache.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    _pipelineCache.TryRemove(key, out _);
                }
            }
        }
    }

    /// <inheritdoc />
    public CentraResiliencePolicyDefinition? GetPolicy(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        return _policies.TryGetValue(policyName, out var def) ? def : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<CentraResiliencePolicyDefinition> GetAllPolicies()
    {
        return _policies.Values.ToArray();
    }

    /// <inheritdoc />
    public IResiliencePipeline GetPipeline(string pipelineName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineName);

        return _pipelineCache.GetOrAdd(pipelineName, name =>
        {
            if (_policies.TryGetValue(name, out var definition))
            {
                return CompilePipeline(definition);
            }

            // Create default generic pipeline
            var defaultDef = CreateDefaultGenericPolicy(name);
            return CompilePipeline(defaultDef);
        });
    }

    /// <inheritdoc />
    public IResiliencePipeline GetServiceInvocationPipeline(string serviceAppId)
    {
        var targetName = $"invocation:{serviceAppId}";
        return _pipelineCache.GetOrAdd(targetName, name =>
        {
            if (_policies.TryGetValue(targetName, out var def))
            {
                return CompilePipeline(def);
            }

            if (_policies.TryGetValue("default-invocation", out var globalDef))
            {
                return CompilePipeline(globalDef with { PolicyName = name });
            }

            var defaultDef = new CentraResiliencePolicyDefinition(
                PolicyName: name,
                Retry: new RetryPolicyOptions(
                    MaxRetries: 3,
                    BackoffType: CentraBackoffType.Exponential,
                    BaseDelay: TimeSpan.FromMilliseconds(100),
                    MaxDelay: TimeSpan.FromSeconds(2),
                    UseJitter: true),
                CircuitBreaker: new CircuitBreakerPolicyOptions(
                    FailureRatio: 0.5,
                    SamplingDuration: TimeSpan.FromSeconds(10),
                    MinimumThroughput: 5,
                    BreakDuration: TimeSpan.FromSeconds(5)),
                Timeout: new TimeoutPolicyOptions(TimeSpan.FromSeconds(10)));

            return CompilePipeline(defaultDef);
        });
    }

    /// <inheritdoc />
    public IResiliencePipeline GetStateStorePipeline(string storeName)
    {
        var targetName = $"state:{storeName}";
        return _pipelineCache.GetOrAdd(targetName, name =>
        {
            if (_policies.TryGetValue(targetName, out var def))
            {
                return CompilePipeline(def);
            }

            if (_policies.TryGetValue("default-state", out var globalDef))
            {
                return CompilePipeline(globalDef with { PolicyName = name });
            }

            var defaultDef = new CentraResiliencePolicyDefinition(
                PolicyName: name,
                Retry: new RetryPolicyOptions(
                    MaxRetries: 3,
                    BackoffType: CentraBackoffType.Exponential,
                    BaseDelay: TimeSpan.FromMilliseconds(50),
                    MaxDelay: TimeSpan.FromSeconds(1),
                    UseJitter: true));

            return CompilePipeline(defaultDef);
        });
    }

    /// <inheritdoc />
    public IResiliencePipeline GetPubSubPipeline(string pubSubName)
    {
        var targetName = $"pubsub:{pubSubName}";
        return _pipelineCache.GetOrAdd(targetName, name =>
        {
            if (_policies.TryGetValue(targetName, out var def))
            {
                return CompilePipeline(def);
            }

            if (_policies.TryGetValue("default-pubsub", out var globalDef))
            {
                return CompilePipeline(globalDef with { PolicyName = name });
            }

            var defaultDef = new CentraResiliencePolicyDefinition(
                PolicyName: name,
                Retry: new RetryPolicyOptions(
                    MaxRetries: 3,
                    BackoffType: CentraBackoffType.Exponential,
                    BaseDelay: TimeSpan.FromMilliseconds(100),
                    MaxDelay: TimeSpan.FromSeconds(2),
                    UseJitter: true));

            return CompilePipeline(defaultDef);
        });
    }

    private IResiliencePipeline CompilePipeline(CentraResiliencePolicyDefinition definition)
    {
        var builder = new ResiliencePipelineBuilder
        {
            TimeProvider = _timeProvider
        };

        var logger = _loggerFactory?.CreateLogger("Centra.Resilience");
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

    private static CentraResiliencePolicyDefinition CreateDefaultGenericPolicy(string name) =>
        new(
            PolicyName: name,
            Retry: new RetryPolicyOptions(
                MaxRetries: 3,
                BackoffType: CentraBackoffType.Exponential,
                BaseDelay: TimeSpan.FromMilliseconds(100),
                MaxDelay: TimeSpan.FromSeconds(2),
            UseJitter: true));

    private static TimeSpan ClampDuration(TimeSpan configured, TimeSpan min, TimeSpan fallback)
    {
        if (configured <= TimeSpan.Zero)
        {
            return fallback;
        }

        return configured < min ? min : configured;
    }
}
