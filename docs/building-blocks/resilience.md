# Distributed Resilience & Fault Tolerance Pipeline

> Enterprise resilience powered directly by Polly Core v8 composite pipelines with live zero-downtime hot-reloading from the Control Plane over Server-Sent Events (SSE).

---

## 🎯 Architecture

Centra integrates Polly Core v8 directly into all execution paths (RPC calls, state mutations, pub/sub dispatch, output bindings, and workflow activities).

A resilience pipeline composes up to 5 strategies in strict order:

```
[Incoming Invocation]
         │
         ▼
    [1. Timeout] ───────────────────> Exceeded? Throw TimeoutRejectedException
         │
         ▼
    [2. Bulkhead / Concurrency] ────> Max concurrent reached? Reject request
         │
         ▼
    [3. Rate Limiter] ──────────────> Quota exceeded? Reject request
         │
         ▼
    [4. Circuit Breaker] ───────────> Open state? Fast-fail immediately
         │
         ▼
    [5. Retry with Jitter] ─────────> Transient failure? Wait & retry
         │
         ▼
 [Target Execution: Database / Broker / Remote Service]
```

---

## 🛠️ Configuring Pipelines in C#

In `Program.cs`:

```csharp
using Centra.Resilience;

builder.Services.AddCentraResilience(options =>
{
    // Configure pipeline for outbound payment gateway
    options.AddPolicy(new CentraResiliencePolicyDefinition(
        PolicyName: "invocation:payment-gateway",
        Retry: new RetryPolicyOptions(
            MaxRetries: 3,
            BackoffType: CentraBackoffType.Exponential,
            BaseDelay: TimeSpan.FromMilliseconds(50),
            MaxDelay: TimeSpan.FromSeconds(2),
            UseJitter: true),
        CircuitBreaker: new CircuitBreakerPolicyOptions(
            FailureRatio: 0.5,
            SamplingDuration: TimeSpan.FromSeconds(10),
            MinimumThroughput: 5,
            BreakDuration: TimeSpan.FromSeconds(5)),
        RateLimiter: new RateLimiterPolicyOptions(
            PermitLimit: 100,
            Window: TimeSpan.FromSeconds(1)),
        Timeout: new TimeoutPolicyOptions(TimeSpan.FromSeconds(3))));

    // Configure fallback pipeline for state stores
    options.AddPolicy(new CentraResiliencePolicyDefinition(
        PolicyName: "default-state",
        Retry: new RetryPolicyOptions(
            MaxRetries: 3,
            BackoffType: CentraBackoffType.Linear,
            BaseDelay: TimeSpan.FromMilliseconds(20))));
});
```

---

## 🔄 Dynamic Hot-Reloading via Control Plane

You can update resilience policies dynamically across all cluster nodes without restarting any processes:

1. Send an HTTP POST to the Control Plane:
   ```bash
   curl -X POST http://control-plane:5000/api/v1/resilience \
     -H "Content-Type: application/json" \
     -d '{
       "policyName": "invocation:payment-gateway",
       "retry": {
         "maxRetries": 5,
         "backoffType": "Exponential",
         "baseDelay": "00:00:00.100",
         "useJitter": true
       },
       "circuitBreaker": {
         "failureRatio": 0.3,
         "samplingDuration": "00:00:15",
         "minimumThroughput": 10,
         "breakDuration": "00:00:10"
       }
     }'
   ```
2. The Control Plane broadcasts a `ResilienceSyncEventDto` over the live SSE stream (`/api/v1/resilience/stream`).
3. Each node's `PollyResiliencePipelineRegistry` receives the event and atomically rebuilds the pipeline.
4. Next invocation immediately uses the new parameters.

---

## 📊 Telemetry & Metrics

The resilience pipeline automatically records OpenTelemetry metrics and activity events:
- `centra.resilience.retries_total`: Counter tracking retry attempts per pipeline.
- `centra.resilience.circuit_state_transitions_total`: Counter tracking transitions between `Closed`, `Open`, and `HalfOpen`.
- `centra.resilience.timeouts_total`: Counter tracking requests rejected by timeout.
- `centra.resilience.rejections_total`: Counter tracking executions rejected by rate limiter or bulkhead.
