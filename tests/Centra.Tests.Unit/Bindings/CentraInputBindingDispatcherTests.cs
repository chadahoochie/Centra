using System.Diagnostics;
using System.Text;
using Centra.Bindings;
using Centra.Tests.Unit.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Bindings;

public sealed class CentraInputBindingDispatcherTests
{
    private readonly CentraInputBindingDispatcher _dispatcher;

    public CentraInputBindingDispatcherTests()
    {
        _dispatcher = new CentraInputBindingDispatcher(NullLogger<CentraInputBindingDispatcher>.Instance);
    }

    [Fact]
    public async Task DispatchAsync_RegisteredTypedHandler_InvokesAndReturnsResponse()
    {
        var bindingName = "orders-in";
        var handler = Substitute.For<IBindingTriggerHandler>();
        var requestData = Encoding.UTF8.GetBytes("inbound payload");
        var responseData = Encoding.UTF8.GetBytes("ack");
        var bindingData = new BindingData(requestData, new Dictionary<string, string> { ["source"] = "queue" }, "application/json");
        var expectedResponse = new BindingResponse(responseData);

        handler.HandleTriggerAsync(bindingData, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BindingResponse>(expectedResponse));

        _dispatcher.RegisterHandler(bindingName, handler);

        var result = await _dispatcher.DispatchAsync(bindingName, bindingData);

        result.Data.ToArray().ShouldBe(responseData);
        await handler.Received(1).HandleTriggerAsync(bindingData, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_RegisteredFuncHandler_InvokesAndReturnsResponse()
    {
        var bindingName = "func-binding";
        var executed = false;

        _dispatcher.RegisterHandler(bindingName, (data, ct) =>
        {
            executed = true;
            return ValueTask.FromResult(new BindingResponse(data.Data));
        });

        var dataBytes = Encoding.UTF8.GetBytes("quick test");
        var result = await _dispatcher.DispatchAsync(bindingName, new BindingData(dataBytes));

        executed.ShouldBeTrue();
        result.Data.ToArray().ShouldBe(dataBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DispatchAsync_InvalidBindingName_ThrowsArgumentException(string invalid)
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
            await _dispatcher.DispatchAsync(invalid, new BindingData(ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public async Task DispatchAsync_UnregisteredBinding_ThrowsInvalidOperationException()
    {
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _dispatcher.DispatchAsync("missing", new BindingData(ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public async Task DispatchAsync_ExtractsW3CTraceContextAndRecordsMetrics()
    {
        using var listener = new TestActivityListener();
        using var meterListener = new TestMeterListener();

        var bindingName = "traced-binding";
        _dispatcher.RegisterHandler(bindingName, (data, ct) => ValueTask.FromResult(new BindingResponse(data.Data)));

        // Create a parent activity to generate traceparent
        using var parentActivity = new Activity("ParentTestActivity").Start();
        var traceparent = parentActivity.Id!;

        var metadata = new Dictionary<string, string>
        {
            ["traceparent"] = traceparent
        };

        var result = await _dispatcher.DispatchAsync(bindingName, new BindingData(Encoding.UTF8.GetBytes("payload"), metadata));

        var span = listener.StoppedActivities.FirstOrDefault(a => a.OperationName == "Centra.Binding.Trigger");
        span.ShouldNotBeNull();
        span.GetTagItem("centra.binding.name")?.ToString().ShouldBe(bindingName);
        span.ParentId.ShouldBe(traceparent);

        var metrics = meterListener.Measurements.Where(m => m.InstrumentName == "centra.binding.triggers.total").ToList();
        metrics.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerThrows_RecordsErrorMetricAndRethrows()
    {
        using var meterListener = new TestMeterListener();
        var bindingName = "failing-trigger";

        _dispatcher.RegisterHandler(bindingName, (data, ct) => throw new ApplicationException("Trigger failed!"));

        await Should.ThrowAsync<ApplicationException>(async () =>
            await _dispatcher.DispatchAsync(bindingName, new BindingData(ReadOnlyMemory<byte>.Empty)));

        var metrics = meterListener.Measurements.Where(m => m.InstrumentName == "centra.binding.triggers.total").ToList();
        metrics.Any(m => m.Tags.Any(t => t.Key == "status" && Equals(t.Value, "error"))).ShouldBeTrue();
    }

    [Fact]
    public void HasHandler_ShouldReturnCorrectStatus()
    {
        _dispatcher.HasHandler("test").ShouldBeFalse();
        _dispatcher.RegisterHandler("test", (_, _) => ValueTask.FromResult(new BindingResponse(ReadOnlyMemory<byte>.Empty)));
        _dispatcher.HasHandler("test").ShouldBeTrue();
    }
}
