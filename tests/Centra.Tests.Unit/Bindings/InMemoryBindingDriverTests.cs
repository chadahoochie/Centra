using System.Text;
using Centra.Bindings;
using Centra.Providers.InMemory.Bindings;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Bindings;

public sealed class InMemoryBindingDriverTests
{
    private readonly InMemoryBindingDriver _driver = new();

    [Fact]
    public async Task InvokeAsync_DefaultBehavior_EchoesRequestData()
    {
        var data = Encoding.UTF8.GetBytes("hello world");
        var metadata = new Dictionary<string, string> { ["header"] = "val" };
        var request = new BindingRequest(data, metadata);

        var response = await _driver.InvokeAsync("test-binding", request);

        response.Data.ToArray().ShouldBe(data);
        response.Metadata.ShouldNotBeNull();
        response.Metadata["header"].ShouldBe("val");
    }

    [Fact]
    public async Task InvokeAsync_CustomHandler_ReturnsCustomResponse()
    {
        _driver.RegisterHandler("transform-binding", (req, ct) =>
        {
            var upper = Encoding.UTF8.GetString(req.Data.Span).ToUpperInvariant();
            return ValueTask.FromResult(new BindingResponse(Encoding.UTF8.GetBytes(upper)));
        });

        var request = new BindingRequest(Encoding.UTF8.GetBytes("lowercase"));
        var response = await _driver.InvokeAsync("transform-binding", request);

        Encoding.UTF8.GetString(response.Data.Span).ShouldBe("LOWERCASE");
    }

    [Fact]
    public async Task TriggerAsync_WhenStarted_DispatchesToHandler()
    {
        BindingData? receivedData = null;
        await _driver.StartAsync((data, ct) =>
        {
            receivedData = data;
            return ValueTask.FromResult(new BindingResponse(Encoding.UTF8.GetBytes("processed")));
        });

        var input = new BindingData(Encoding.UTF8.GetBytes("incoming trigger"), new Dictionary<string, string> { ["source"] = "test" });
        var result = await _driver.TriggerAsync(input);

        result.Data.ToArray().ShouldBe(Encoding.UTF8.GetBytes("processed"));
        receivedData.ShouldNotBeNull();
        receivedData.Value.Data.ToArray().ShouldBe(Encoding.UTF8.GetBytes("incoming trigger"));
    }

    [Fact]
    public async Task TriggerAsync_WhenNotStarted_ThrowsInvalidOperationException()
    {
        var input = new BindingData(ReadOnlyMemory<byte>.Empty);
        await Should.ThrowAsync<InvalidOperationException>(async () => await _driver.TriggerAsync(input));
    }

    [Fact]
    public async Task StopAsync_PreventsFurtherTriggers()
    {
        await _driver.StartAsync((data, ct) => ValueTask.FromResult(new BindingResponse(data.Data)));
        await _driver.StopAsync();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _driver.TriggerAsync(new BindingData(ReadOnlyMemory<byte>.Empty)));
    }
}
