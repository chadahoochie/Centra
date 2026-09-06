using System.Collections.Concurrent;
using Centra.Bindings;
using Centra.Drivers;

namespace Centra.Providers.InMemory.Bindings;

public sealed class InMemoryBindingDriver : IBindingDriver
{
    private readonly ConcurrentDictionary<string, Func<BindingRequest, CancellationToken, ValueTask<BindingResponse>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

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
}
