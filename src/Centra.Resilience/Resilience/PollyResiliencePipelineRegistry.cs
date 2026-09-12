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

    internal void InvalidateCache(string policyName)
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
                return PollyPipelineCompiler.Compile(definition, _timeProvider, _loggerFactory);
            }

            // Create default generic pipeline
            var defaultDef = PollyPipelineCompiler.CreateDefaultGenericPolicy(name);
            return PollyPipelineCompiler.Compile(defaultDef, _timeProvider, _loggerFactory);
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
                return PollyPipelineCompiler.Compile(def, _timeProvider, _loggerFactory);
            }

            if (_policies.TryGetValue("default-invocation", out var globalDef))
            {
                return PollyPipelineCompiler.Compile(globalDef with { PolicyName = name }, _timeProvider, _loggerFactory);
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

            return PollyPipelineCompiler.Compile(defaultDef, _timeProvider, _loggerFactory);
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
                return PollyPipelineCompiler.Compile(def, _timeProvider, _loggerFactory);
            }

            if (_policies.TryGetValue("default-state", out var globalDef))
            {
                return PollyPipelineCompiler.Compile(globalDef with { PolicyName = name }, _timeProvider, _loggerFactory);
            }

            var defaultDef = new CentraResiliencePolicyDefinition(
                PolicyName: name,
                Retry: new RetryPolicyOptions(
                    MaxRetries: 3,
                    BackoffType: CentraBackoffType.Exponential,
                    BaseDelay: TimeSpan.FromMilliseconds(50),
                    MaxDelay: TimeSpan.FromSeconds(1),
                    UseJitter: true));

            return PollyPipelineCompiler.Compile(defaultDef, _timeProvider, _loggerFactory);
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
                return PollyPipelineCompiler.Compile(def, _timeProvider, _loggerFactory);
            }

            if (_policies.TryGetValue("default-pubsub", out var globalDef))
            {
                return PollyPipelineCompiler.Compile(globalDef with { PolicyName = name }, _timeProvider, _loggerFactory);
            }

            var defaultDef = new CentraResiliencePolicyDefinition(
                PolicyName: name,
                Retry: new RetryPolicyOptions(
                    MaxRetries: 3,
                    BackoffType: CentraBackoffType.Exponential,
                    BaseDelay: TimeSpan.FromMilliseconds(100),
                    MaxDelay: TimeSpan.FromSeconds(2),
                    UseJitter: true));

            return PollyPipelineCompiler.Compile(defaultDef, _timeProvider, _loggerFactory);
        });
    }
}
