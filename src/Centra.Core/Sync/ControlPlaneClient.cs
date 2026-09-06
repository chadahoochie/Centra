using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Centra.Components;
using Centra.Sync;

namespace Centra.Sync;

public sealed class ControlPlaneClient : IControlPlaneClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public ControlPlaneClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyCollection<ComponentDefinition>> GetComponentsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<List<ComponentDefinition>>(
            "api/v1/components",
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        return response ?? (IReadOnlyCollection<ComponentDefinition>)Array.Empty<ComponentDefinition>();
    }

    public async Task<IReadOnlyCollection<ServiceNodeDto>> GetTopologyAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetFromJsonAsync<List<ServiceNodeDto>>(
            "api/v1/topology",
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        return response ?? (IReadOnlyCollection<ServiceNodeDto>)Array.Empty<ServiceNodeDto>();
    }

    public async IAsyncEnumerable<ComponentSyncEventDto> StreamUpdatesAsync(
        string appId,
        string instanceId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestUri = $"api/v1/sync/stream?appId={Uri.EscapeDataString(appId)}&instanceId={Uri.EscapeDataString(instanceId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var evt in SseStreamReader.ReadEventsAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            yield return evt;
        }
    }

    public async Task SendHeartbeatAsync(
        string appId,
        string instanceId,
        string status,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            appId,
            instanceId,
            status,
            metadata
        };

        var response = await _httpClient.PostAsJsonAsync("api/v1/heartbeat", payload, JsonOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
