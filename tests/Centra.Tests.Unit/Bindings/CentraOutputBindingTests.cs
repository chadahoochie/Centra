using System.Text;
using Centra.Bindings;
using Centra.Drivers;
using Centra.Registry;
using Centra.Resilience;
using Centra.Tests.Unit.Common;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Bindings;

public sealed class CentraOutputBindingTests
{
    private readonly ComponentRegistry _registry;
    private readonly IBindingDriver _driver;

    public CentraOutputBindingTests()
    {
        _registry = new ComponentRegistry();
        _driver = Substitute.For<IBindingDriver>();
    }

    [Fact]
    public void Constructor_NullRegistry_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new CentraOutputBinding(null!));
    }

    [Fact]
    public async Task InvokeAsync_ValidBinding_CallsDriverAndReturnsResponse()
    {
        var bindingName = "my-binding";
        _registry.RegisterBindingDriver(bindingName, _driver);

        var requestData = Encoding.UTF8.GetBytes("ping");
        var responseData = Encoding.UTF8.GetBytes("pong");
        var request = new BindingRequest(requestData, new Dictionary<string, string> { ["req"] = "1" }, "echo");
        var expectedResponse = new BindingResponse(responseData, new Dictionary<string, string> { ["res"] = "1" });

        _driver.InvokeAsync(bindingName, request, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BindingResponse>(expectedResponse));

        var outputBinding = new CentraOutputBinding(_registry);

        var result = await outputBinding.InvokeAsync(bindingName, request);

        result.Data.ToArray().ShouldBe(responseData);
        result.Metadata.ShouldNotBeNull();
        result.Metadata["res"].ShouldBe("1");
        await _driver.Received(1).InvokeAsync(bindingName, request, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_InvalidName_ThrowsArgumentException(string invalid)
    {
        var outputBinding = new CentraOutputBinding(_registry);
        await Should.ThrowAsync<ArgumentException>(async () =>
            await outputBinding.InvokeAsync(invalid, new BindingRequest(ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public async Task InvokeAsync_UnregisteredBinding_ThrowsInvalidOperationException()
    {
        var outputBinding = new CentraOutputBinding(_registry);
        var request = new BindingRequest(ReadOnlyMemory<byte>.Empty);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await outputBinding.InvokeAsync("nonexistent-binding", request));
    }

    [Fact]
    public async Task InvokeAsync_WithResiliencePipeline_ExecutesThroughPipeline()
    {
        var bindingName = "resilient-binding";
        _registry.RegisterBindingDriver(bindingName, _driver);

        var resilienceProvider = Substitute.For<IResiliencePipelineProvider>();
        var pipeline = Substitute.For<IResiliencePipeline>();

        resilienceProvider.GetPipeline($"binding:{bindingName}").Returns(pipeline);

        var request = new BindingRequest(ReadOnlyMemory<byte>.Empty);
        var expectedResponse = new BindingResponse(Encoding.UTF8.GetBytes("resilient-response"));

        pipeline.ExecuteAsync(Arg.Any<Func<CancellationToken, ValueTask<BindingResponse>>>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        var outputBinding = new CentraOutputBinding(_registry, resilienceProvider);

        var result = await outputBinding.InvokeAsync(bindingName, request);

        result.Data.ToArray().ShouldBe(Encoding.UTF8.GetBytes("resilient-response"));
        await pipeline.Received(1).ExecuteAsync(Arg.Any<Func<CancellationToken, ValueTask<BindingResponse>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_GeneratesActivityAndMetrics()
    {
        using var listener = new TestActivityListener();
        using var meterListener = new TestMeterListener();

        var bindingName = "telemetry-binding";
        _registry.RegisterBindingDriver(bindingName, _driver);

        _driver.InvokeAsync(bindingName, Arg.Any<BindingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BindingResponse>(new BindingResponse(ReadOnlyMemory<byte>.Empty)));

        var outputBinding = new CentraOutputBinding(_registry);

        await outputBinding.InvokeAsync(bindingName, new BindingRequest(ReadOnlyMemory<byte>.Empty, null, "send"));

        var span = listener.StoppedActivities.FirstOrDefault(a => a.OperationName == "Centra.Binding.Invoke");
        span.ShouldNotBeNull();
        span.GetTagItem("centra.binding.name")?.ToString().ShouldBe(bindingName);

        var metrics = meterListener.Measurements.Where(m => m.InstrumentName == "centra.binding.invocations.total").ToList();
        metrics.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_WhenDriverThrows_SetsActivityErrorAndThrows()
    {
        using var listener = new TestActivityListener();
        using var meterListener = new TestMeterListener();

        var bindingName = "failing-binding";
        _registry.RegisterBindingDriver(bindingName, _driver);

        _driver.InvokeAsync(bindingName, Arg.Any<BindingRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException<BindingResponse>(new InvalidOperationException("Driver exploded")));

        var outputBinding = new CentraOutputBinding(_registry);

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await outputBinding.InvokeAsync(bindingName, new BindingRequest(ReadOnlyMemory<byte>.Empty, null, "fail-op")));

        ex.Message.ShouldBe("Driver exploded");

        var span = listener.StoppedActivities.FirstOrDefault(a => a.OperationName == "Centra.Binding.Invoke");
        span.ShouldNotBeNull();
        span.Status.ShouldBe(System.Diagnostics.ActivityStatusCode.Error);

        var metrics = meterListener.Measurements.Where(m => m.InstrumentName == "centra.binding.invocations.total").ToList();
        metrics.ShouldNotBeEmpty();
        metrics.Any(m => m.Tags.Any(t => t.Key == "status" && t.Value?.ToString() == "error")).ShouldBeTrue();
    }
}
