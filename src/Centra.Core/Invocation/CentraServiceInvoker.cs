using System.Diagnostics;
using System.Net.Http.Headers;
using Centra.Diagnostics;
using Centra.Events;
using Centra.Memory;
using Centra.Serialization;

namespace Centra.Invocation;

public sealed class CentraServiceInvoker : IServiceInvoker
{
    private readonly HttpClient _httpClient;
    private readonly ICentraSerializer _serializer;
    private readonly IServiceEndpointResolver _endpointResolver;

    public CentraServiceInvoker(
        HttpClient httpClient,
        ICentraSerializer? serializer = null,
        IServiceEndpointResolver? endpointResolver = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serializer = serializer ?? JsonCentraSerializer.Default;
        _endpointResolver = endpointResolver ?? PassThroughServiceEndpointResolver.Instance;
    }

    public async ValueTask<TResponse> InvokeMethodAsync<TRequest, TResponse>(
        string serviceAppId,
        string methodName,
        TRequest request,
        string? httpVerb = null,
        ServiceInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var requestPayload = _serializer.Serialize(request);
        var responsePayload = await InvokeMethodRawAsync(
            serviceAppId,
            methodName,
            requestPayload,
            httpVerb,
            options?.Headers,
            options,
            cancellationToken).ConfigureAwait(false);

        var result = _serializer.Deserialize<TResponse>(responsePayload);
        if (result is null)
        {
            throw new InvalidOperationException($"Failed to deserialize response from service '{serviceAppId}' method '{methodName}' into type '{typeof(TResponse).Name}'");
        }

        return result;
    }

    public async ValueTask<ReadOnlyMemory<byte>> InvokeMethodRawAsync(
        string serviceAppId,
        string methodName,
        ReadOnlyMemory<byte> payload,
        string? httpVerb = null,
        IReadOnlyDictionary<string, string>? headers = null,
        ServiceInvocationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var verb = new HttpMethod(httpVerb ?? "POST");
        var baseUri = await _endpointResolver.ResolveEndpointAsync(serviceAppId, cancellationToken).ConfigureAwait(false)
            ?? new Uri($"http://{serviceAppId}/", UriKind.Absolute);

        var uri = new Uri(baseUri, methodName.TrimStart('/'));

        using var requestMessage = new HttpRequestMessage(verb, uri);

        if (!payload.IsEmpty)
        {
            var content = new ReadOnlyMemoryContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            requestMessage.Content = content;
        }

        // Trace Context & Ambient IDs
        var traceHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CentraTracePropagator.Inject(Activity.Current, traceHeaders);

        foreach (var kvp in traceHeaders)
        {
            requestMessage.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        if (CentraAmbientContext.CorrelationId is not null)
        {
            requestMessage.Headers.TryAddWithoutValidation(CloudEventConstants.CorrelationIdHeader, CentraAmbientContext.CorrelationId);
        }

        if (CentraAmbientContext.CausationId is not null)
        {
            requestMessage.Headers.TryAddWithoutValidation(CloudEventConstants.CausationIdHeader, CentraAmbientContext.CausationId);
        }

        if (CentraAmbientContext.TenantId is not null)
        {
            requestMessage.Headers.TryAddWithoutValidation(CloudEventConstants.TenantIdHeader, CentraAmbientContext.TenantId);
        }

        if (headers is not null)
        {
            foreach (var kvp in headers)
            {
                requestMessage.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
            }
        }

        var startTime = Stopwatch.GetTimestamp();
        using var activity = CentraDiagnostics.StartInvokeClientActivity(serviceAppId, methodName);

        try
        {
            using var response = await _httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordInvocation(serviceAppId, methodName, "success", durationMs);

            return responseBytes;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            CentraMeters.RecordInvocation(serviceAppId, methodName, "error", durationMs);
            throw;
        }
    }
}
