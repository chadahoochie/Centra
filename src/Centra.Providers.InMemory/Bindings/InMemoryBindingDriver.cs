using System.Collections.Concurrent;
using Centra.Bindings;
using Centra.Drivers;

namespace Centra.Providers.InMemory.Bindings;

public sealed class InMemoryBindingDriver : IBindingDriver, IInputBinding
{
    private readonly ConcurrentDictionary<string, Func<BindingRequest, CancellationToken, ValueTask<BindingResponse>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private Func<BindingData, CancellationToken, ValueTask<BindingResponse>>? _triggerHandler;
    private bool _isStarted;

    public void RegisterHandler(string bindingName, Func<BindingRequest, CancellationToken, ValueTask<BindingResponse>> handler)
    {
        _handlers[bindingName] = handler;
    }

    public ValueTask<BindingResponse> InvokeAsync(
        string bindingName,
        BindingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_handlers.TryGetValue(bindingName, out var handler))
        {
            return handler(request, cancellationToken);
        }

        // Echo response as default in-memory behavior
        return new ValueTask<BindingResponse>(new BindingResponse(request.Data, request.Metadata));
    }

    public ValueTask StartAsync(
        Func<BindingData, CancellationToken, ValueTask<BindingResponse>> handler,
        CancellationToken cancellationToken = default)
    {
        _triggerHandler = handler ?? throw new ArgumentNullException(nameof(handler));
        _isStarted = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _isStarted = false;
        _triggerHandler = null;
        return ValueTask.CompletedTask;
    }

    public ValueTask<BindingResponse> TriggerAsync(
        BindingData data,
        CancellationToken cancellationToken = default)
    {
        if (!_isStarted || _triggerHandler is null)
        {
            throw new InvalidOperationException("In-memory binding is not running or no trigger handler is attached.");
        }

        return _triggerHandler(data, cancellationToken);
    }
}
