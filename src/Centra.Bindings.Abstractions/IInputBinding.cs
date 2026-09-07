namespace Centra.Bindings;

public interface IInputBinding
{
    ValueTask StartAsync(
        Func<BindingData, CancellationToken, ValueTask<BindingResponse>> handler,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
