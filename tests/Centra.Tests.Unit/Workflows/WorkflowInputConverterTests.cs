using Centra.Core.Workflows;
using Centra.Serialization;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class WorkflowInputConverterTests
{
    private readonly ICentraSerializer _serializer = Substitute.For<ICentraSerializer>();
    private readonly WorkflowInputConverter _converter = WorkflowInputConverter.Instance;

    [Fact]
    public void ConvertInput_WithNullTargetType_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            _converter.ConvertInput("data", null!, _serializer));
    }

    [Fact]
    public void ConvertInput_WithNullSerializer_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            _converter.ConvertInput("data", typeof(string), null!));
    }

    [Fact]
    public void ConvertInput_NullInput_ReturnsDefault_ForValueType()
    {
        var result = _converter.ConvertInput(null, typeof(int), _serializer);
        result.ShouldBe(0);

        var dateResult = _converter.ConvertInput(null, typeof(DateTime), _serializer);
        dateResult.ShouldBe(default(DateTime));
    }

    [Fact]
    public void ConvertInput_NullInput_ReturnsNull_ForReferenceType()
    {
        var result = _converter.ConvertInput(null, typeof(string), _serializer);
        result.ShouldBeNull();
    }

    [Fact]
    public void ConvertInput_DirectTypeMatch_ReturnsInputDirectly()
    {
        var input = "already a string";
        var result = _converter.ConvertInput(input, typeof(string), _serializer);
        result.ShouldBeSameAs(input);
        _serializer.DidNotReceiveWithAnyArgs().Deserialize(default(byte[]), default!);
    }

    [Fact]
    public void ConvertInput_ByteArray_DeserializesUsingByteArray()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var expected = new TestWorkflowDto("payload");
        _serializer.Deserialize(bytes, typeof(TestWorkflowDto)).Returns(expected);

        var result = _converter.ConvertInput(bytes, typeof(TestWorkflowDto), _serializer);
        result.ShouldBeSameAs(expected);
        _serializer.Received(1).Deserialize(bytes, typeof(TestWorkflowDto));
    }

    [Fact]
    public void ConvertInput_ReadOnlyMemoryByte_DeserializesUsingMemory()
    {
        ReadOnlyMemory<byte> memory = new byte[] { 4, 5, 6 };
        var expected = new TestWorkflowDto("memory");
        _serializer.Deserialize(memory, typeof(TestWorkflowDto)).Returns(expected);

        var result = _converter.ConvertInput(memory, typeof(TestWorkflowDto), _serializer);
        result.ShouldBeSameAs(expected);
        _serializer.Received(1).Deserialize(memory, typeof(TestWorkflowDto));
    }

    [Fact]
    public void ConvertInput_DifferentObjectType_SerializesAndDeserializes()
    {
        var input = new { Name = "test" };
        var serializedBytes = new byte[] { 10, 20 };
        var expected = new TestWorkflowDto("test");

        _serializer.Serialize(input).Returns(serializedBytes);
        _serializer.Deserialize(serializedBytes, typeof(TestWorkflowDto)).Returns(expected);

        var result = _converter.ConvertInput(input, typeof(TestWorkflowDto), _serializer);
        result.ShouldBeSameAs(expected);
        _serializer.Received(1).Serialize(input);
        _serializer.Received(1).Deserialize(serializedBytes, typeof(TestWorkflowDto));
    }

    private sealed record TestWorkflowDto(string Value);
}
