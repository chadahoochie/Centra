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

        using var httpRequest = HttpWebhookBindingMessageConverter.CreateRequest(bindingName, request, _logger);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var responseMetadata = HttpWebhookBindingMessageConverter.BuildResponseMetadata(response);

        return new BindingResponse(responseBytes, responseMetadata);
    }
}
