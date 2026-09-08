using System;
using Centra.Memory;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Memory;

public sealed class PooledByteBufferWriterTests
{
    [Fact]
    public void Should_Initialize_With_Default_Capacity()
    {
        // Arrange & Act
        using var sut = new PooledByteBufferWriter();

        // Assert
        sut.Capacity.ShouldBeGreaterThanOrEqualTo(256);
        sut.WrittenCount.ShouldBe(0);
        sut.FreeCapacity.ShouldBe(sut.Capacity);
    }

    [Fact]
    public void Should_Initialize_With_Custom_Capacity()
    {
        // Arrange
        var customCapacity = 1024;

        // Act
        using var sut = new PooledByteBufferWriter(customCapacity);

        // Assert
        sut.Capacity.ShouldBeGreaterThanOrEqualTo(customCapacity);
        sut.WrittenCount.ShouldBe(0);
    }

    [Fact]
    public void Should_Advance_And_Track_WrittenCount()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(100);
        var memory = sut.GetMemory(10);
        
        // Act
        sut.Advance(10);

        // Assert
        sut.WrittenCount.ShouldBe(10);
        sut.FreeCapacity.ShouldBe(sut.Capacity - 10);
    }

    [Fact]
    public void Should_Throw_InvalidOperationException_When_Advance_Beyond_Buffer()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(100);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => sut.Advance(sut.Capacity + 1));
    }

    [Fact]
    public void Should_Throw_ArgumentOutOfRangeException_When_Advance_Negative()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(100);

        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => sut.Advance(-1));
    }

    [Fact]
    public void Should_Return_Correct_WrittenMemory_After_Write()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(100);
        var span = sut.GetSpan(5);
        for (byte i = 0; i < 5; i++)
        {
            span[i] = i;
        }
        
        // Act
        sut.Advance(5);

        // Assert
        var memory = sut.WrittenMemory;
        memory.Length.ShouldBe(5);
        var array = memory.ToArray();
        array.ShouldBe([0, 1, 2, 3, 4]);
    }

    [Fact]
    public void Should_Return_Correct_WrittenSpan_After_Write()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(100);
        var span = sut.GetSpan(3);
        span[0] = 10;
        span[1] = 20;
        span[2] = 30;
        
        // Act
        sut.Advance(3);

        // Assert
        var writtenSpan = sut.WrittenSpan;
        writtenSpan.Length.ShouldBe(3);
        writtenSpan.ToArray().ShouldBe([10, 20, 30]);
    }

    [Fact]
    public void Should_Resize_Buffer_When_GetMemory_Exceeds_Capacity()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(10);
        var initialCapacity = sut.Capacity;

        // Act
        var memory = sut.GetMemory(initialCapacity + 10);

        // Assert
        sut.Capacity.ShouldBeGreaterThanOrEqualTo(initialCapacity + 10);
        memory.Length.ShouldBeGreaterThanOrEqualTo(initialCapacity + 10);
    }

    [Fact]
    public void Should_Resize_Buffer_When_GetSpan_Exceeds_Capacity()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(10);
        var initialCapacity = sut.Capacity;

        // Act
        var span = sut.GetSpan(initialCapacity + 20);

        // Assert
        sut.Capacity.ShouldBeGreaterThanOrEqualTo(initialCapacity + 20);
        span.Length.ShouldBeGreaterThanOrEqualTo(initialCapacity + 20);
    }

    [Fact]
    public void Should_Reset_WrittenCount_To_Zero()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(100);
        sut.GetSpan(10);
        sut.Advance(10);
        
        // Act
        sut.Reset();

        // Assert
        sut.WrittenCount.ShouldBe(0);
        sut.WrittenMemory.Length.ShouldBe(0);
    }

    [Fact]
    public void Should_ToArray_Return_Written_Bytes()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(100);
        var span = sut.GetSpan(2);
        span[0] = 42;
        span[1] = 43;
        sut.Advance(2);

        // Act
        var result = sut.ToArray();

        // Assert
        result.ShouldNotBeNull();
        result.Length.ShouldBe(2);
        result.ShouldBe([42, 43]);
    }

    [Fact]
    public void Should_Dispose_And_Zero_Capacity()
    {
        // Arrange
        var sut = new PooledByteBufferWriter(100);
        sut.GetSpan(5);
        sut.Advance(5);

        // Act
        sut.Dispose();

        // Assert
        sut.Capacity.ShouldBe(0);
        sut.WrittenCount.ShouldBe(0);
        sut.FreeCapacity.ShouldBe(0);
    }

    [Fact]
    public void Should_Handle_GetMemory_With_SizeHint_Zero()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(100);

        // Act
        var memory = sut.GetMemory(0);

        // Assert
        memory.Length.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Should_Throw_ArgumentOutOfRangeException_When_GetMemory_Negative_SizeHint()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(100);

        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => sut.GetMemory(-1));
    }

    [Fact]
    public void Should_Preserve_Data_After_Resize()
    {
        // Arrange
        using var sut = new PooledByteBufferWriter(10);
        var initialSpan = sut.GetSpan(5);
        for (byte i = 0; i < 5; i++)
        {
            initialSpan[i] = i;
        }
        sut.Advance(5);

        var initialCapacity = sut.Capacity;

        // Act
        var nextSpan = sut.GetSpan(initialCapacity + 10);
        for (byte i = 5; i < 15; i++)
        {
            nextSpan[i - 5] = i;
        }
        sut.Advance(10);

        // Assert
        var result = sut.ToArray();
        result.Length.ShouldBe(15);
        var expected = new byte[15];
        for (byte i = 0; i < 15; i++) expected[i] = i;
        result.ShouldBe(expected);
    }
}
