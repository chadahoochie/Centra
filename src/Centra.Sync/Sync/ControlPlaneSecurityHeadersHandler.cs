using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Centra.Sync;

public sealed class ControlPlaneSecurityHeadersHandler : DelegatingHandler
{
    private readonly CentraControlPlaneOptions _options;
    private readonly ControlPlaneEndpointSelector _selector;
    private readonly TimeProvider _timeProvider;

    public ControlPlaneSecurityHeadersHandler(
        CentraControlPlaneOptions options,
        ControlPlaneEndpointSelector selector,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clusterId = string.IsNullOrWhiteSpace(_options.ClusterId) ? "default" : _options.ClusterId;
        request.Headers.TryAddWithoutValidation("X-Centra-Cluster-Id", clusterId);

        if (_options.UseHmacAuthentication && !string.IsNullOrWhiteSpace(_options.ClusterToken))
        {
            var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString();
            var nonce = Guid.NewGuid().ToString("N");
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var payload = $"{clusterId}:{path}:{timestamp}:{nonce}";

            Span<byte> secretBytes = stackalloc byte[Encoding.UTF8.GetByteCount(_options.ClusterToken)];
            Encoding.UTF8.GetBytes(_options.ClusterToken, secretBytes);

            Span<byte> payloadBytes = stackalloc byte[Encoding.UTF8.GetByteCount(payload)];
            Encoding.UTF8.GetBytes(payload, payloadBytes);

            Span<byte> hashBuffer = stackalloc byte[32];
            HMACSHA256.HashData(secretBytes, payloadBytes, hashBuffer);

            var signatureHex = Convert.ToHexString(hashBuffer);

            request.Headers.TryAddWithoutValidation("X-Centra-Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("X-Centra-Nonce", nonce);
            request.Headers.TryAddWithoutValidation("X-Centra-Signature", signatureHex);
        }
        else if (!string.IsNullOrWhiteSpace(_options.ClusterToken))
        {
            request.Headers.TryAddWithoutValidation("X-Centra-Cluster-Token", _options.ClusterToken);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.TemporaryRedirect || response.Headers.Contains("X-Centra-Leader"))
        {
            if (response.Headers.TryGetValues("X-Centra-Leader", out var leaderValues))
            {
                var leaderEndpoint = leaderValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(leaderEndpoint))
                {
                    _selector.SetCurrentLeader(leaderEndpoint);
                }
            }
            else if (response.Headers.Location is not null)
            {
                var leaderAuthority = response.Headers.Location.GetLeftPart(UriPartial.Authority);
                _selector.SetCurrentLeader(leaderAuthority);
            }
        }

        return response;
    }
}
