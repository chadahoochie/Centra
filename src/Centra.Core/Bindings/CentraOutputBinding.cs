using Centra.Bindings;
using Centra.Drivers;
using Centra.Registry;
using Centra.Resilience;

namespace Centra.Bindings;

public sealed class CentraOutputBinding : IOutputBinding
{
    private readonly ComponentRegistry _registry;
    private readonly IResiliencePipelineProvider? _resilienceProvider;

    public CentraOutputBinding(
        ComponentRegistry registry,
        IResiliencePipelineProvider? resilienceProvider = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _resilienceProvider = resilienceProvider;
    }

    public async ValueTask<BindingResponse> InvokeAsync(
        string bindingName,
        BindingRequest request,
        CancellationToken cancellationToken = default)
    {
        var driver = _registry.GetBindingDriver(bindingName);
        if (driver is null)
        {
            throw new InvalidOperationException($"No Binding driver registered for binding '{bindingName}'");
        }

        if (_resilienceProvider is not null)
        {
            var pipeline = _resilienceProvider.GetPipeline($"binding:{bindingName}");
            return await pipeline.ExecuteAsync(async ct => await driver.InvokeAsync(bindingName, request, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }

        return await driver.InvokeAsync(bindingName, request, cancellationToken).ConfigureAwait(false);
    }
}
