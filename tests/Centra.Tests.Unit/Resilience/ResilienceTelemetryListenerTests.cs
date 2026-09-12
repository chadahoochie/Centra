using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Resilience;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Resilience;

public sealed class ResilienceTelemetryListenerTests
{
    [Fact]
    public void Constructor_NullPipelineName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new ResilienceTelemetryListener(null!));
    }

    [Fact]
    public async Task OnRetry_WithActiveActivityAndLogger_EnrichesActivityAndLogs()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CentraDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CentraDiagnostics.Source.StartActivity("TestActivity");
        activity.ShouldNotBeNull();

        var logger = Substitute.For<ILogger>();
        var sut = new ResilienceTelemetryListener("test-pipeline", logger);

        var context = ResilienceContextPool.Shared.Get();
        var outcome = Outcome.FromException<string>(new InvalidOperationException("network error"));
        var args = new OnRetryArguments<string>(context, outcome, 1, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(50));

        await sut.OnRetry(args);

        activity.GetTagItem("centra.resilience.retry_count").ShouldBe(1);
        activity.GetTagItem("centra.resilience.last_error").ShouldBe("InvalidOperationException");

        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task OnRetry_WithoutActivityAndNoException_CompletesSuccessfully()
    {
        var sut = new ResilienceTelemetryListener("test-pipeline");
        var context = ResilienceContextPool.Shared.Get();
        var outcome = Outcome.FromResult("success");
        var args = new OnRetryArguments<string>(context, outcome, 2, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20));

        await sut.OnRetry(args);

        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task OnCircuitOpened_EnrichesActivityAndLogs()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CentraDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CentraDiagnostics.Source.StartActivity("TestCircuitActivity");
        var logger = Substitute.For<ILogger>();
        var sut = new ResilienceTelemetryListener("cb-pipeline", logger);

        var context = ResilienceContextPool.Shared.Get();
        var outcome = Outcome.FromException<string>(new InvalidOperationException("failure"));
        var args = new OnCircuitOpenedArguments<string>(context, outcome, TimeSpan.FromSeconds(5), false);

        await sut.OnCircuitOpened(args);

        activity?.GetTagItem("centra.resilience.circuit_state").ShouldBe("Open");

        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task OnCircuitClosed_EnrichesActivityAndLogs()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CentraDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CentraDiagnostics.Source.StartActivity("TestCircuitClosedActivity");
        var logger = Substitute.For<ILogger>();
        var sut = new ResilienceTelemetryListener("cb-pipeline", logger);

        var context = ResilienceContextPool.Shared.Get();
        var outcome = Outcome.FromResult("recovered");
        var args = new OnCircuitClosedArguments<string>(context, outcome, false);

        await sut.OnCircuitClosed(args);

        activity?.GetTagItem("centra.resilience.circuit_state").ShouldBe("Closed");

        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task OnCircuitHalfOpened_EnrichesActivityAndLogs()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CentraDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CentraDiagnostics.Source.StartActivity("TestCircuitHalfOpenedActivity");
        var logger = Substitute.For<ILogger>();
        var sut = new ResilienceTelemetryListener("cb-pipeline", logger);

        var context = ResilienceContextPool.Shared.Get();
        var args = new OnCircuitHalfOpenedArguments(context);

        await sut.OnCircuitHalfOpened(args);

        activity?.GetTagItem("centra.resilience.circuit_state").ShouldBe("HalfOpen");

        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task OnTimeout_EnrichesActivityAndLogs()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CentraDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CentraDiagnostics.Source.StartActivity("TestTimeoutActivity");
        var logger = Substitute.For<ILogger>();
        var sut = new ResilienceTelemetryListener("timeout-pipeline", logger);

        var context = ResilienceContextPool.Shared.Get();
        var args = new OnTimeoutArguments(context, TimeSpan.FromSeconds(3));

        await sut.OnTimeout(args);

        activity?.GetTagItem("centra.resilience.timeout").ShouldBe(true);

        ResilienceContextPool.Shared.Return(context);
    }
}
