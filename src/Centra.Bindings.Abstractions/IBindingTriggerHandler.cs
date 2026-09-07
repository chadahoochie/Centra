namespace Centra.Bindings;

public interface IBindingTriggerHandler
{
    ValueTask<BindingResponse> HandleTriggerAsync(
        BindingData data,
        CancellationToken cancellationToken = default);
}
