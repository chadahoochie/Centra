using Microsoft.AspNetCore.Http;

namespace Centra.Hosting.Extensions;

internal static class CentraHttpEndpointHelpers
{
    internal static void ApplyResponseHeaders(HttpResponse response, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        foreach (var (k, v) in metadata)
        {
            if (k.StartsWith("header:", StringComparison.OrdinalIgnoreCase))
            {
                response.Headers.TryAdd(k["header:".Length..], v);
            }
        }
    }

    internal static string ResolveResponseContentType(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is not null && metadata.TryGetValue("content-type", out var ct))
        {
            return ct;
        }

        return "application/octet-stream";
    }

    internal static async ValueTask<byte[]> ReadRequestBodyBytesAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength.HasValue)
        {
            var length = (int)request.ContentLength.Value;
            if (length <= 0)
            {
                return [];
            }

            var buffer = GC.AllocateUninitializedArray<byte>(length);
            await request.Body.ReadExactlyAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            return buffer;
        }

        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }
}
