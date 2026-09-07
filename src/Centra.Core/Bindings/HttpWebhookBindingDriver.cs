using System.Net.Http.Headers;
using Centra.Bindings;
using Centra.Drivers;
using Microsoft.Extensions.Logging;

namespace Centra.Bindings;

public sealed class HttpWebhookBindingDriver : IBindingDriver
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpWebhookBindingDriver>? _logger;

    public HttpWebhookBindingDriver(
        HttpClient httpClient,
        ILogger<HttpWebhookBindingDriver>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
    }

    public async ValueTask<BindingResponse> InvokeAsync(
        string bindingName,
        BindingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);

        var targetUrl = ResolveTargetUrl(bindingName, request);
        var method = ResolveHttpMethod(request.Operation);

        using var httpRequest = new HttpRequestMessage(method, targetUrl);
        ApplyContent(httpRequest, request, method);
        ApplyCustomHeaders(httpRequest, request.Metadata);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var responseMetadata = BuildResponseMetadata(response);

        return new BindingResponse(responseBytes, responseMetadata);
    }

    private string ResolveTargetUrl(string bindingName, BindingRequest request)
    {
        string? targetUrl = null;
        if (request.Metadata is not null && request.Metadata.TryGetValue("url", out var urlVal))
        {
            targetUrl = urlVal;
        }

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            _logger?.LogError("Binding '{BindingName}' invocation failed: Missing required 'url' in metadata.", bindingName);
            throw new InvalidOperationException($"Binding request for '{bindingName}' is missing required 'url' metadata.");
        }

        return targetUrl;
    }

    private static HttpMethod ResolveHttpMethod(string? operation)
    {
        return string.IsNullOrWhiteSpace(operation)
            ? HttpMethod.Post
            : new HttpMethod(operation.ToUpperInvariant());
    }

    private static void ApplyContent(HttpRequestMessage httpRequest, BindingRequest request, HttpMethod method)
    {
        if (request.Data.IsEmpty || method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Delete)
        {
            return;
        }

        var content = new ByteArrayContent(request.Data.ToArray());
        var contentType = "application/json";
        if (request.Metadata is not null && request.Metadata.TryGetValue("content-type", out var ctVal) && !string.IsNullOrWhiteSpace(ctVal))
        {
            contentType = ctVal;
        }
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        httpRequest.Content = content;
    }

    private static void ApplyCustomHeaders(HttpRequestMessage httpRequest, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return;

        foreach (var (key, value) in metadata)
        {
            if (key.StartsWith("header:", StringComparison.OrdinalIgnoreCase))
            {
                var headerName = key["header:".Length..];
                httpRequest.Headers.TryAddWithoutValidation(headerName, value);
            }
        }
    }

    private static Dictionary<string, string> BuildResponseMetadata(HttpResponseMessage response)
    {
        var responseMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["status_code"] = ((int)response.StatusCode).ToString()
        };

        foreach (var header in response.Headers)
        {
            responseMetadata[$"header:{header.Key}"] = string.Join(", ", header.Value);
        }

        return responseMetadata;
    }
}
