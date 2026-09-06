namespace Centra.Bindings;

public interface IOutputBinding
{
    ValueTask<BindingResponse> InvokeAsync(
        string bindingName,
        BindingRequest request,
        CancellationToken cancellationToken = default);
}
