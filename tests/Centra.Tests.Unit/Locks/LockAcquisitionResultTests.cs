using Centra.Locks;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Locks;

public sealed class LockAcquisitionResultTests
{
    [Fact]
    public void Constructor_Should_Set_Properties_Correctly()
    {
        var mockLock = Substitute.For<IDistributedLock>();
        var resultSuccess = new LockAcquisitionResult(true, mockLock);
        var resultFailed = new LockAcquisitionResult(false, null);

        resultSuccess.Succeeded.ShouldBeTrue();
        resultSuccess.Lock.ShouldBeSameAs(mockLock);

        resultFailed.Succeeded.ShouldBeFalse();
        resultFailed.Lock.ShouldBeNull();
    }

    [Fact]
    public void Deconstruct_Should_Unpack_Properties()
    {
        var mockLock = Substitute.For<IDistributedLock>();
        var result = new LockAcquisitionResult(true, mockLock);

        var (succeeded, acquiredLock) = result;

        succeeded.ShouldBeTrue();
        acquiredLock.ShouldBeSameAs(mockLock);
    }

    [Fact]
    public void Equality_Should_Honor_Value_Semantics()
    {
        var mockLock = Substitute.For<IDistributedLock>();
        var result1 = new LockAcquisitionResult(true, mockLock);
        var result2 = new LockAcquisitionResult(true, mockLock);
        var result3 = new LockAcquisitionResult(false, null);

        result1.ShouldBe(result2);
        (result1 == result2).ShouldBeTrue();
        (result1 != result3).ShouldBeTrue();
        result1.GetHashCode().ShouldBe(result2.GetHashCode());
    }
}
