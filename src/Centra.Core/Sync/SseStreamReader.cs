using System.Runtime.CompilerServices;
using System.Text.Json;
using Centra.Sync;

namespace Centra.Sync;

public static class SseStreamReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async IAsyncEnumerable<ComponentSyncEventDto> ReadEventsAsync(
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
                if (dataLines.Count > 0)
                {
                    var fullData = string.Join("\n", dataLines);
                    dataLines.Clear();

                    ComponentSyncEventDto? dto = null;
                    try
                    {
                        dto = JsonSerializer.Deserialize<ComponentSyncEventDto>(fullData, JsonOptions);
                    }
                    catch
                    {
                        // Ignore malformed lines to prevent crashing the stream
                    }

                    if (dto is not null)
                    {
                        yield return dto;
                    }
                }
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var data = line.Substring("data:".Length).Trim();
                dataLines.Add(data);
            }
        }
    }
}
