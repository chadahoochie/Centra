using System.Collections.Concurrent;
using System.Diagnostics;
using Centra.Bindings;
using Centra.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Centra.Bindings;

public sealed class CentraInputBindingDispatcher
{
    private readonly ConcurrentDictionary<string, Func<BindingData, CancellationToken, ValueTask<BindingResponse>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<CentraInputBindingDispatcher>? _logger;

    public CentraInputBindingDispatcher(ILogger<CentraInputBindingDispatcher>? logger = null)
    {
        _logger = logger;
    }

    public void RegisterHandler(string bindingName, IBindingTriggerHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[bindingName] = handler.HandleTriggerAsync;
    }

    public void RegisterHandler(string bindingName, Func<BindingData, CancellationToken, ValueTask<BindingResponse>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[bindingName] = handler;
    }

    public bool HasHandler(string bindingName)
    {
        return !string.IsNullOrWhiteSpace(bindingName) && _handlers.ContainsKey(bindingName);
    }

    public async ValueTask<BindingResponse> DispatchAsync(
        string bindingName,
        BindingData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);

        if (!_handlers.TryGetValue(bindingName, out var handler))
        {
            _logger?.LogWarning("No handler registered for input binding '{BindingName}'.", bindingName);
            throw new InvalidOperationException($"No handler registered for input binding '{bindingName}'.");
        }

        var parentContext = data.Metadata is not null
            ? CentraTracePropagator.Extract(data.Metadata)
            : default;

        using var activity = CentraDiagnostics.StartBindingInputActivity(bindingName, parentContext);
        var startTime = Stopwatch.GetTimestamp();

        try
        {
            var response = await handler(data, cancellationToken).ConfigureAwait(false);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordBindingTrigger(bindingName, "success", durationMs);
            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordBindingTrigger(bindingName, "error", durationMs);
            _logger?.LogError(ex, "Error processing input binding trigger for '{BindingName}'.", bindingName);
            throw;
        }
    }
}
