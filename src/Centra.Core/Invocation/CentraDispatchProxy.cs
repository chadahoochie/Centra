using System.Collections.Concurrent;
using System.Reflection;

namespace Centra.Invocation;

public class CentraDispatchProxy : DispatchProxy
{
    private static readonly ConcurrentDictionary<MethodInfo, CentraDispatchMethodMetadata> MetadataCache = new();

    private IServiceInvoker _invoker = null!;
    private string _appId = null!;

    public void Initialize(IServiceInvoker invoker, string appId)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _appId = appId ?? throw new ArgumentNullException(nameof(appId));
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var metadata = MetadataCache.GetOrAdd(targetMethod, static m => CentraDispatchMethodMetadata.Create(m));

        var ct = metadata.CancellationTokenIndex >= 0 && args is not null
            ? (CancellationToken)(args[metadata.CancellationTokenIndex] ?? CancellationToken.None)
            : CancellationToken.None;

        var requestBody = metadata.BodyParameterIndex >= 0 && args is not null
            ? args[metadata.BodyParameterIndex]
            : null;

        return metadata.Invoker(_invoker, _appId, metadata.MethodName, metadata.HttpVerb, requestBody, ct);
    }
}
