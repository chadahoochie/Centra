using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Centra.Bindings;

internal static class HttpWebhookBindingMessageConverter
{
    internal static HttpRequestMessage CreateRequest(string bindingName, BindingRequest request, ILogger? logger)
    {
        var targetUrl = ResolveTargetUrl(bindingName, request, logger);
        var method = ResolveHttpMethod(request.Operation);

        var httpRequest = new HttpRequestMessage(method, targetUrl);
        ApplyContent(httpRequest, request, method);
        ApplyCustomHeaders(httpRequest, request.Metadata);

        return httpRequest;
    }

    internal static string ResolveTargetUrl(string bindingName, BindingRequest request, ILogger? logger)
    {
        string? targetUrl = null;
        if (request.Metadata is not null && request.Metadata.TryGetValue("url", out var urlVal))
        {
            targetUrl = urlVal;
        }

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            logger?.LogError("Binding '{BindingName}' invocation failed: Missing required 'url' in metadata.", bindingName);
            throw new InvalidOperationException($"Binding request for '{bindingName}' is missing required 'url' metadata.");
        }

        return targetUrl;
    }

    internal static HttpMethod ResolveHttpMethod(string? operation)
    {
        return string.IsNullOrWhiteSpace(operation)
            ? HttpMethod.Post
            : new HttpMethod(operation.ToUpperInvariant());
    }

    internal static void ApplyContent(HttpRequestMessage httpRequest, BindingRequest request, HttpMethod method)
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

    internal static void ApplyCustomHeaders(HttpRequestMessage httpRequest, IReadOnlyDictionary<string, string>? metadata)
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

    internal static Dictionary<string, string> BuildResponseMetadata(HttpResponseMessage response)
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
