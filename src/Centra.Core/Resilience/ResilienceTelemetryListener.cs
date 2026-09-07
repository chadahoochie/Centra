using System.Diagnostics;
using Centra.Diagnostics;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Centra.Resilience;

/// <summary>
/// Hooks into Polly v8 events to enrich OpenTelemetry distributed tracing and metrics.
/// </summary>
public sealed class ResilienceTelemetryListener
{
    private readonly string _pipelineName;
    private readonly ILogger? _logger;

    public ResilienceTelemetryListener(string pipelineName, ILogger? logger = null)
    {
        _pipelineName = pipelineName ?? throw new ArgumentNullException(nameof(pipelineName));
        _logger = logger;
    }

    public ValueTask OnRetry<T>(OnRetryArguments<T> args)
    {
        var attempt = args.AttemptNumber;
        var delay = args.RetryDelay;
        var ex = args.Outcome.Exception;
        var exType = ex?.GetType().Name;

        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("centra.resilience.retry_count", attempt);
            if (exType is not null)
            {
                activity.SetTag("centra.resilience.last_error", exType);
            }
        }

        CentraMeters.RecordResilienceRetry(_pipelineName, attempt, exType);

        if (_logger is not null)
        {
            CentraLogMessages.LogResilienceRetry(
                _logger,
                _pipelineName,
                attempt,
                delay.TotalMilliseconds,
                ex?.Message ?? "Unsuccessful outcome");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnCircuitOpened<T>(OnCircuitOpenedArguments<T> args)
    {
        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("centra.resilience.circuit_state", "Open");
        }

        CentraMeters.RecordResilienceCircuitTransition(_pipelineName, "Open");

        if (_logger is not null)
        {
            CentraLogMessages.LogResilienceCircuitOpened(_logger, _pipelineName, args.BreakDuration.TotalMilliseconds);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnCircuitClosed<T>(OnCircuitClosedArguments<T> args)
    {
        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("centra.resilience.circuit_state", "Closed");
        }

        CentraMeters.RecordResilienceCircuitTransition(_pipelineName, "Closed");

        if (_logger is not null)
        {
            CentraLogMessages.LogResilienceCircuitClosed(_logger, _pipelineName);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnCircuitHalfOpened(OnCircuitHalfOpenedArguments args)
    {
        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("centra.resilience.circuit_state", "HalfOpen");
        }

        CentraMeters.RecordResilienceCircuitTransition(_pipelineName, "HalfOpen");

        if (_logger is not null)
        {
            CentraLogMessages.LogResilienceCircuitHalfOpened(_logger, _pipelineName);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnTimeout(OnTimeoutArguments args)
    {
        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("centra.resilience.timeout", true);
        }

        CentraMeters.RecordResilienceTimeout(_pipelineName, args.Timeout.TotalSeconds);

        if (_logger is not null)
        {
            CentraLogMessages.LogResilienceTimeout(_logger, _pipelineName, args.Timeout.TotalMilliseconds);
        }

        return ValueTask.CompletedTask;
    }
}
