using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Centra.Sync;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Sync;

public sealed class SseStreamReaderTests
{
    private record TestSseEvent(string Name, int Value);

    private static Stream CreateStream(string sseData)
    {
        var bytes = Encoding.UTF8.GetBytes(sseData);
        return new MemoryStream(bytes);
    }

    [Fact]
    public async Task Should_Parse_Single_SSE_Event_When_Valid()
    {
        // Arrange
        var data = "data: {\"name\":\"test1\",\"value\":42}\n\n";
        using var stream = CreateStream(data);

        // Act
        var events = new List<TestSseEvent>();
        await foreach (var evt in SseStreamReader.ReadEventsAsync<TestSseEvent>(stream))
        {
            events.Add(evt);
        }

        // Assert
        events.Count.ShouldBe(1);
        events[0].Name.ShouldBe("test1");
        events[0].Value.ShouldBe(42);
    }

    [Fact]
    public async Task Should_Parse_Multiple_SSE_Events_When_Valid()
    {
        // Arrange
        var data = "data: {\"name\":\"test1\",\"value\":42}\n\ndata: {\"name\":\"test2\",\"value\":100}\n\n";
        using var stream = CreateStream(data);

        // Act
        var events = new List<TestSseEvent>();
        await foreach (var evt in SseStreamReader.ReadEventsAsync<TestSseEvent>(stream))
        {
            events.Add(evt);
        }

        // Assert
        events.Count.ShouldBe(2);
        events[0].Name.ShouldBe("test1");
        events[0].Value.ShouldBe(42);
        events[1].Name.ShouldBe("test2");
        events[1].Value.ShouldBe(100);
    }

    [Fact]
    public async Task Should_Skip_Empty_Lines_Between_Events_When_Multiple_Newlines()
    {
        // Arrange
        var data = "data: {\"name\":\"test1\",\"value\":42}\n\n\n\n\ndata: {\"name\":\"test2\",\"value\":100}\n\n";
        using var stream = CreateStream(data);

        // Act
        var events = new List<TestSseEvent>();
        await foreach (var evt in SseStreamReader.ReadEventsAsync<TestSseEvent>(stream))
        {
            events.Add(evt);
        }

        // Assert
        events.Count.ShouldBe(2);
        events[0].Name.ShouldBe("test1");
        events[1].Name.ShouldBe("test2");
    }

    [Fact]
    public async Task Should_Handle_Malformed_Json_Gracefully_When_Event_Is_Invalid()
    {
        // Arrange
        var data = "data: {\"name\":\"test1\",\"value\":bad}\n\ndata: {\"name\":\"test2\",\"value\":100}\n\n";
        using var stream = CreateStream(data);

        // Act
        var events = new List<TestSseEvent>();
        await foreach (var evt in SseStreamReader.ReadEventsAsync<TestSseEvent>(stream))
        {
            events.Add(evt);
        }

        // Assert
        events.Count.ShouldBe(1);
        events[0].Name.ShouldBe("test2");
        events[0].Value.ShouldBe(100);
    }

    [Fact]
    public async Task Should_Handle_Multi_Line_Data_Events_When_Data_Split_Across_Lines()
    {
        // Arrange
        var data = "data: {\"name\":\"test1\",\ndata: \"value\":42}\n\n";
        using var stream = CreateStream(data);

        // Act
        var events = new List<TestSseEvent>();
        await foreach (var evt in SseStreamReader.ReadEventsAsync<TestSseEvent>(stream))
        {
            events.Add(evt);
        }

        // Assert
        events.Count.ShouldBe(1);
        events[0].Name.ShouldBe("test1");
        events[0].Value.ShouldBe(42);
    }

    [Fact]
    public async Task Should_Return_Empty_When_Stream_Is_Empty()
    {
        // Arrange
        using var stream = CreateStream("");

        // Act
        var events = new List<TestSseEvent>();
        await foreach (var evt in SseStreamReader.ReadEventsAsync<TestSseEvent>(stream))
        {
            events.Add(evt);
        }

        // Assert
        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_Ignore_Non_Data_Lines_When_Event_Contains_Id_Or_Event()
    {
        // Arrange
        var data = "id: 123\nevent: update\ndata: {\"name\":\"test1\",\"value\":42}\n\n";
        using var stream = CreateStream(data);

        // Act
        var events = new List<TestSseEvent>();
        await foreach (var evt in SseStreamReader.ReadEventsAsync<TestSseEvent>(stream))
        {
            events.Add(evt);
        }

        // Assert
        events.Count.ShouldBe(1);
        events[0].Name.ShouldBe("test1");
        events[0].Value.ShouldBe(42);
    }

    [Fact]
    public async Task Should_Read_ComponentSyncEvents_Via_Convenience_Method_When_Stream_Has_Component_Events()
    {
        // Arrange
        var data = "data: {\"action\":0,\"componentName\":\"test-component\"}\n\n";
        using var stream = CreateStream(data);

        // Act
        var events = new List<ComponentSyncEventDto>();
        await foreach (var evt in SseStreamReader.ReadEventsAsync(stream))
        {
            events.Add(evt);
        }

        // Assert
        events.Count.ShouldBe(1);
        events[0].ComponentName.ShouldBe("test-component");
    }

    [Fact]
    public async Task Should_Read_ResilienceSyncEvents_Via_Convenience_Method_When_Stream_Has_Resilience_Events()
    {
        // Arrange
        var data = "data: {\"action\":\"Updated\",\"policyName\":\"my-policy\"}\n\n";
        using var stream = CreateStream(data);

        // Act
        var events = new List<ResilienceSyncEventDto>();
        await foreach (var evt in SseStreamReader.ReadResilienceEventsAsync(stream))
        {
            events.Add(evt);
        }

        // Assert
        events.Count.ShouldBe(1);
        events[0].Action.ShouldBe("Updated");
        events[0].PolicyName.ShouldBe("my-policy");
    }
}
