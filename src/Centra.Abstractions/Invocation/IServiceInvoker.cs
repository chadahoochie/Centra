namespace Centra.Invocation;

public interface IServiceInvoker
{
    ValueTask<TResponse> InvokeMethodAsync<TRequest, TResponse>(
        string serviceAppId,
        string methodName,
        TRequest request,
        string? httpVerb = null,
        ServiceInvocationOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<ReadOnlyMemory<byte>> InvokeMethodRawAsync(
        string serviceAppId,
        string methodName,
        ReadOnlyMemory<byte> payload,
        string? httpVerb = null,
        IReadOnlyDictionary<string, string>? headers = null,
        ServiceInvocationOptions? options = null,
        CancellationToken cancellationToken = default);
}
