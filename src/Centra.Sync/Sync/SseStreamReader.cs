using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Centra.Sync;

public static class SseStreamReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async IAsyncEnumerable<T> ReadEventsAsync<T>(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);
        var dataLines = new List<string>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (dataLines.Count == 0)
                {
                    continue;
                }

                var fullData = string.Join("\n", dataLines);
                T? dto = default;
                try
                {
                    dto = JsonSerializer.Deserialize<T>(fullData, JsonOptions);
                }
                catch (JsonException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SseStreamReader] Malformed SSE event data dropped: {ex.Message}");
                }

                if (dto is not null)
                {
                    yield return dto;
                }

                dataLines.Clear();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var data = line.Substring("data:".Length).Trim();
                dataLines.Add(data);
            }
        }
    }

    public static IAsyncEnumerable<ComponentSyncEventDto> ReadEventsAsync(
        Stream stream,
        CancellationToken cancellationToken = default) =>
        ReadEventsAsync<ComponentSyncEventDto>(stream, cancellationToken);

    public static IAsyncEnumerable<ResilienceSyncEventDto> ReadResilienceEventsAsync(
        Stream stream,
        CancellationToken cancellationToken = default) =>
        ReadEventsAsync<ResilienceSyncEventDto>(stream, cancellationToken);
}
