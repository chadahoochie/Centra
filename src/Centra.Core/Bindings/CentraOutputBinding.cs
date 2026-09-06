using Centra.Bindings;
using Centra.Drivers;
using Centra.Registry;

namespace Centra.Bindings;

public sealed class CentraOutputBinding : IOutputBinding
{
    private readonly ComponentRegistry _registry;

    public CentraOutputBinding(ComponentRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ValueTask<BindingResponse> InvokeAsync(
        string bindingName,
        BindingRequest request,
        CancellationToken cancellationToken = default)
    {
        var driver = _registry.GetBindingDriver(bindingName);
        if (driver is null)
        {
            throw new InvalidOperationException($"No Binding driver registered for binding '{bindingName}'");
        }

        return driver.InvokeAsync(bindingName, request, cancellationToken);
    }
}
