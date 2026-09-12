using System.Net;
using Centra.Sample.Resilience.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.Resilience.Chaos;

public sealed class FlakyPaymentGatewayServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly PaymentChaosState _state;

    public PaymentChaosState State => _state;

    internal FlakyPaymentGatewayServer(WebApplication app, PaymentChaosState state)
    {
        _app = app;
        _state = state;
    }

    public static async Task<FlakyPaymentGatewayServer> StartAsync(PaymentChaosState? state = null)
    {
        var chaosState = state ?? new PaymentChaosState();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var app = builder.Build();

        app.MapPost("/api/v1/payments/process", async (PaymentRequest request, HttpContext context, CancellationToken ct) =>
        {
            chaosState.IncrementRequests();

            switch (chaosState.Mode)
            {
                case PaymentChaosMode.TransientBlip:
                    var blipNum = chaosState.IncrementTransientBlips();
                    if (blipNum <= 2)
                    {
                        chaosState.IncrementFailures();
                        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "Transient payment gateway error",
                            attempt = blipNum
                        }, ct);
                        return;
                    }

                    // Recovered
                    chaosState.IncrementSuccesses();
                    await context.Response.WriteAsJsonAsync(new PaymentResponse(
                        Guid.NewGuid().ToString("N")[..12],
                        request.OrderId,
                        "Approved",
                        $"Processed successfully on retry attempt {blipNum}"), ct);
                    return;

                case PaymentChaosMode.Outage:
                    chaosState.IncrementFailures();
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Payment gateway core outage"
                    }, ct);
                    return;

                case PaymentChaosMode.LatencySpike:
                    // Spike delay to trigger caller timeout
                    await Task.Delay(2000, ct);
                    chaosState.IncrementSuccesses();
                    await context.Response.WriteAsJsonAsync(new PaymentResponse(
                        Guid.NewGuid().ToString("N")[..12],
                        request.OrderId,
                        "Approved",
                        "Processed after latency spike"), ct);
                    return;

                case PaymentChaosMode.Normal:
                default:
                    chaosState.IncrementSuccesses();
                    await context.Response.WriteAsJsonAsync(new PaymentResponse(
                        Guid.NewGuid().ToString("N")[..12],
                        request.OrderId,
                        "Approved",
                        "Processed cleanly without faults"), ct);
                    return;
            }
        });

        await app.StartAsync();
        return new FlakyPaymentGatewayServer(app, chaosState);
    }

    public HttpClient CreateClient() => _app.GetTestServer().CreateClient();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
