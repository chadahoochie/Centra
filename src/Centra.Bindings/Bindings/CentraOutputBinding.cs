using System.Diagnostics;
using Centra.Bindings;
using Centra.Diagnostics;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);

        var driver = _registry.GetBindingDriver(bindingName);
        if (driver is null)
        {
            throw new InvalidOperationException($"No Binding driver registered for binding '{bindingName}'");
        }

        using var activity = CentraDiagnostics.StartBindingOutputActivity(bindingName, request.Operation);
        var startTime = Stopwatch.GetTimestamp();

        try
        {
            BindingResponse response;

            if (_resilienceProvider is not null)
            {
                var pipeline = _resilienceProvider.GetPipeline($"binding:{bindingName}");
                response = await pipeline.ExecuteAsync(
                    async ct => await driver.InvokeAsync(bindingName, request, ct).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                response = await driver.InvokeAsync(bindingName, request, cancellationToken).ConfigureAwait(false);
            }

            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordBindingInvocation(bindingName, request.Operation ?? "default", "success", durationMs);
            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordBindingInvocation(bindingName, request.Operation ?? "default", "error", durationMs);
            throw;
        }
    }
}
