using System;
using Centra.Core.Actors;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class Md5ConsistentHashAlgorithmTests
{
    [Fact]
    public void ComputeHash_Should_Throw_ArgumentNullException_When_Key_Is_Null()
    {
        // Arrange
        var sut = Md5ConsistentHashAlgorithm.Instance;

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => sut.ComputeHash(null!));
    }

    [Fact]
    public void ComputeHash_Should_Hash_Short_Key_Using_Stackalloc()
    {
        // Arrange
        var sut = Md5ConsistentHashAlgorithm.Instance;
        var key = "short-actor-key-123";

        // Act
        var hash1 = sut.ComputeHash(key);
        var hash2 = sut.ComputeHash(key);

        // Assert
        hash1.ShouldBe(hash2);
        hash1.ShouldNotBe(0u);
    }

    [Fact]
    public void ComputeHash_Should_Hash_Long_Key_Exceeding_256_Bytes()
    {
        // Arrange
        var sut = Md5ConsistentHashAlgorithm.Instance;
        var longKey = new string('A', 300);

        // Act
        var hash1 = sut.ComputeHash(longKey);
        var hash2 = sut.ComputeHash(longKey);

        // Assert
        hash1.ShouldBe(hash2);
        hash1.ShouldNotBe(0u);
    }
}
