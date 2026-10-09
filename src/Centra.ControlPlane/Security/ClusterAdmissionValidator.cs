using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Centra.ControlPlane.Security;

public sealed class ClusterAdmissionValidator : IClusterAdmissionValidator
{
    private readonly ControlPlaneSecurityOptions _options;
    private readonly ClusterAdmissionNonceCache _nonceCache;
    private readonly TimeProvider _timeProvider;

    public ClusterAdmissionValidator(
        ControlPlaneSecurityOptions options,
        ClusterAdmissionNonceCache? nonceCache = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _nonceCache = nonceCache ?? new ClusterAdmissionNonceCache();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<ClusterAdmissionResult> ValidateAsync(HttpContext httpContext, string clusterId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return ValueTask.FromResult(ClusterAdmissionResult.Success());
        }

        if (!string.IsNullOrWhiteSpace(_options.AdminToken) &&
            httpContext.Request.Headers.TryGetValue("X-Centra-Admin-Token", out var adminHeader))
        {
            var adminHeaderStr = adminHeader.ToString();
            Span<byte> adminHeaderBytes = stackalloc byte[Encoding.UTF8.GetByteCount(adminHeaderStr)];
            Encoding.UTF8.GetBytes(adminHeaderStr, adminHeaderBytes);

            Span<byte> configuredAdminBytes = stackalloc byte[Encoding.UTF8.GetByteCount(_options.AdminToken)];
            Encoding.UTF8.GetBytes(_options.AdminToken, configuredAdminBytes);

            if (CryptographicOperations.FixedTimeEquals(adminHeaderBytes, configuredAdminBytes))
            {
                return ValueTask.FromResult(ClusterAdmissionResult.Success());
            }
        }

        if (string.IsNullOrWhiteSpace(clusterId))
        {
            return ValueTask.FromResult(ClusterAdmissionResult.BadRequest("ClusterId is required for admission."));
        }

        if (!_options.ClusterTokens.TryGetValue(clusterId, out var configuredSecret) || string.IsNullOrWhiteSpace(configuredSecret))
        {
            return ValueTask.FromResult(ClusterAdmissionResult.Unauthorized($"Cluster '{clusterId}' is not authorized or token is not configured."));
        }

        if (_options.RequireHmacSignature)
        {
            if (!httpContext.Request.Headers.TryGetValue("X-Centra-Timestamp", out var tsHeader) ||
                !httpContext.Request.Headers.TryGetValue("X-Centra-Nonce", out var nonceHeader) ||
                !httpContext.Request.Headers.TryGetValue("X-Centra-Signature", out var sigHeader))
            {
                return ValueTask.FromResult(ClusterAdmissionResult.Unauthorized("Missing HMAC authentication headers (Timestamp, Nonce, or Signature)."));
            }

            var tsStr = tsHeader.ToString();
            if (!long.TryParse(tsStr, out var unixSeconds))
            {
                return ValueTask.FromResult(ClusterAdmissionResult.Unauthorized("Invalid timestamp format in authentication header."));
            }

            var now = _timeProvider.GetUtcNow();
            var requestTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            var drift = (now - requestTime).Duration();
            if (drift > _options.AllowedClockDrift)
            {
                return ValueTask.FromResult(ClusterAdmissionResult.Unauthorized("Clock drift exceeded allowed window."));
            }

            var nonceStr = nonceHeader.ToString();
            if (string.IsNullOrWhiteSpace(nonceStr) || !_nonceCache.TryAddNonce(nonceStr, now, _options.AllowedClockDrift * 2))
            {
                return ValueTask.FromResult(ClusterAdmissionResult.Unauthorized("Replay detected: nonce has already been used or is empty."));
            }

            var path = httpContext.Request.Path.Value ?? string.Empty;
            var payload = $"{clusterId}:{path}:{tsStr}:{nonceStr}";

            Span<byte> secretBytes = stackalloc byte[Encoding.UTF8.GetByteCount(configuredSecret)];
            Encoding.UTF8.GetBytes(configuredSecret, secretBytes);

            Span<byte> payloadBytes = stackalloc byte[Encoding.UTF8.GetByteCount(payload)];
            Encoding.UTF8.GetBytes(payload, payloadBytes);

            Span<byte> hashBuffer = stackalloc byte[32];
            HMACSHA256.HashData(secretBytes, payloadBytes, hashBuffer);

            var sigStr = sigHeader.ToString();
            byte[] expectedSigBytes;
            try
            {
                expectedSigBytes = Convert.FromHexString(sigStr);
            }
            catch (FormatException)
            {
                return ValueTask.FromResult(ClusterAdmissionResult.Unauthorized("Invalid signature encoding format."));
            }

            if (!CryptographicOperations.FixedTimeEquals(hashBuffer, expectedSigBytes))
            {
                return ValueTask.FromResult(ClusterAdmissionResult.Unauthorized("Invalid HMAC signature."));
            }
        }
        else
        {
            if (!httpContext.Request.Headers.TryGetValue("X-Centra-Cluster-Token", out var tokenHeader))
            {
                return ValueTask.FromResult(ClusterAdmissionResult.Unauthorized("Missing X-Centra-Cluster-Token authentication header."));
            }

            var tokenStr = tokenHeader.ToString();
            Span<byte> tokenBytes = stackalloc byte[Encoding.UTF8.GetByteCount(tokenStr)];
            Encoding.UTF8.GetBytes(tokenStr, tokenBytes);

            Span<byte> secretBytes = stackalloc byte[Encoding.UTF8.GetByteCount(configuredSecret)];
            Encoding.UTF8.GetBytes(configuredSecret, secretBytes);

            if (!CryptographicOperations.FixedTimeEquals(tokenBytes, secretBytes))
            {
                return ValueTask.FromResult(ClusterAdmissionResult.Unauthorized("Invalid cluster token."));
            }
        }

        if (_options.RequireRemoteIpMatch)
        {
            if (httpContext.Request.Headers.TryGetValue("X-Centra-Reported-Ip", out var reportedIpHeader))
            {
                var reportedIp = reportedIpHeader.ToString();
                var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
                if (!string.Equals(reportedIp, remoteIp, StringComparison.OrdinalIgnoreCase))
                {
                    return ValueTask.FromResult(ClusterAdmissionResult.Forbidden("Reported IP address does not match connection remote IP."));
                }
            }
        }

        return ValueTask.FromResult(ClusterAdmissionResult.Success());
    }
}
