using System.Security.Cryptography;
using System.Text;
using Centra.ControlPlane.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Security;

public sealed class ClusterAdmissionValidatorTests
{
    [Fact]
    public async Task When_Security_Disabled_Should_Allow_Any_Request()
    {
        var options = new ControlPlaneSecurityOptions { Enabled = false };
        var validator = new ClusterAdmissionValidator(options);
        var httpContext = new DefaultHttpContext();

        var result = await validator.ValidateAsync(httpContext, "cluster-1");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task When_Security_Enabled_Missing_Cluster_Token_Should_Reject_401()
    {
        var options = new ControlPlaneSecurityOptions
        {
            Enabled = true,
            ClusterTokens = { ["cluster-1"] = "secret-token-123" }
        };
        var validator = new ClusterAdmissionValidator(options);
        var httpContext = new DefaultHttpContext();

        var result = await validator.ValidateAsync(httpContext, "cluster-1");

        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task When_Security_Enabled_Invalid_Cluster_Token_Should_Reject_401()
    {
        var options = new ControlPlaneSecurityOptions
        {
            Enabled = true,
            ClusterTokens = { ["cluster-1"] = "secret-token-123" }
        };
        var validator = new ClusterAdmissionValidator(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Centra-Cluster-Token"] = "wrong-token";

        var result = await validator.ValidateAsync(httpContext, "cluster-1");

        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task When_Security_Enabled_Valid_Cluster_Token_Should_Allow()
    {
        var options = new ControlPlaneSecurityOptions
        {
            Enabled = true,
            ClusterTokens = { ["cluster-1"] = "secret-token-123" }
        };
        var validator = new ClusterAdmissionValidator(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Centra-Cluster-Token"] = "secret-token-123";

        var result = await validator.ValidateAsync(httpContext, "cluster-1");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task When_Admin_Token_Provided_Should_Bypass_Cluster_Check()
    {
        var options = new ControlPlaneSecurityOptions
        {
            Enabled = true,
            AdminToken = "super-admin-secret",
            ClusterTokens = { ["cluster-1"] = "secret-token-123" }
        };
        var validator = new ClusterAdmissionValidator(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Centra-Admin-Token"] = "super-admin-secret";

        var result = await validator.ValidateAsync(httpContext, "unconfigured-cluster");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task When_RequireHmacSignature_Valid_Signature_Should_Allow()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var secret = "hmac-secret-key-32-bytes-long!!";
        var options = new ControlPlaneSecurityOptions
        {
            Enabled = true,
            RequireHmacSignature = true,
            ClusterTokens = { ["cluster-1"] = secret }
        };
        var validator = new ClusterAdmissionValidator(options, timeProvider: timeProvider);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/heartbeat";
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");
        var payload = $"cluster-1:/api/v1/heartbeat:{timestamp}:{nonce}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signatureHex = Convert.ToHexString(signatureBytes);

        httpContext.Request.Headers["X-Centra-Timestamp"] = timestamp;
        httpContext.Request.Headers["X-Centra-Nonce"] = nonce;
        httpContext.Request.Headers["X-Centra-Signature"] = signatureHex;

        var result = await validator.ValidateAsync(httpContext, "cluster-1");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task When_RequireHmacSignature_Replayed_Nonce_Should_Reject_401()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var secret = "hmac-secret-key-32-bytes-long!!";
        var options = new ControlPlaneSecurityOptions
        {
            Enabled = true,
            RequireHmacSignature = true,
            ClusterTokens = { ["cluster-1"] = secret }
        };
        var validator = new ClusterAdmissionValidator(options, timeProvider: timeProvider);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/heartbeat";
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString();
        var nonce = "fixed-nonce-12345";
        var payload = $"cluster-1:/api/v1/heartbeat:{timestamp}:{nonce}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signatureHex = Convert.ToHexString(signatureBytes);

        httpContext.Request.Headers["X-Centra-Timestamp"] = timestamp;
        httpContext.Request.Headers["X-Centra-Nonce"] = nonce;
        httpContext.Request.Headers["X-Centra-Signature"] = signatureHex;

        var firstResult = await validator.ValidateAsync(httpContext, "cluster-1");
        firstResult.IsSuccess.ShouldBeTrue();

        // Second call with same nonce must be rejected
        var replayResult = await validator.ValidateAsync(httpContext, "cluster-1");
        replayResult.IsSuccess.ShouldBeFalse();
        replayResult.StatusCode.ShouldBe(401);
        replayResult.ErrorMessage.ShouldNotBeNull();
        replayResult.ErrorMessage.ShouldContain("Replay");
    }

    [Fact]
    public async Task When_RequireHmacSignature_Timestamp_Drift_Exceeded_Should_Reject_401()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var secret = "hmac-secret-key-32-bytes-long!!";
        var options = new ControlPlaneSecurityOptions
        {
            Enabled = true,
            RequireHmacSignature = true,
            AllowedClockDrift = TimeSpan.FromSeconds(30),
            ClusterTokens = { ["cluster-1"] = secret }
        };
        var validator = new ClusterAdmissionValidator(options, timeProvider: timeProvider);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/heartbeat";
        // 60 seconds in the past -> exceeds 30s drift
        var staleTimestamp = timeProvider.GetUtcNow().AddSeconds(-60).ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");
        var payload = $"cluster-1:/api/v1/heartbeat:{staleTimestamp}:{nonce}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signatureHex = Convert.ToHexString(signatureBytes);

        httpContext.Request.Headers["X-Centra-Timestamp"] = staleTimestamp;
        httpContext.Request.Headers["X-Centra-Nonce"] = nonce;
        httpContext.Request.Headers["X-Centra-Signature"] = signatureHex;

        var result = await validator.ValidateAsync(httpContext, "cluster-1");

        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(401);
        result.ErrorMessage.ShouldNotBeNull();
        result.ErrorMessage.ShouldContain("Clock drift");
    }
}
