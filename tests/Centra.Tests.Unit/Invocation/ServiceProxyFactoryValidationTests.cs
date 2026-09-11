using Centra.Invocation;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Invocation;

public sealed class ServiceProxyFactoryValidationTests
{
    private readonly IServiceInvoker _invoker = Substitute.For<IServiceInvoker>();

    private class ConcreteClassService
    {
    }

    private interface IUnattributedService
    {
        Task DoSomethingAsync();
    }

    [ServiceClient("")]
    private interface IEmptyAppIdService
    {
        Task DoSomethingAsync();
    }

    [ServiceClient("valid-app")]
    private interface IValidService
    {
        Task DoSomethingAsync();
    }

    [Fact]
    public void Create_NullInvoker_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            ServiceProxyFactory.Create<IValidService>(null!));
    }

    [Fact]
    public void Create_ConcreteClass_ThrowsInvalidOperationException()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            ServiceProxyFactory.Create<ConcreteClassService>(_invoker));

        ex.Message.ShouldContain("must be an interface");
    }

    [Fact]
    public void Create_InterfaceWithoutAttribute_ThrowsInvalidOperationException()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            ServiceProxyFactory.Create<IUnattributedService>(_invoker));

        ex.Message.ShouldContain("must be decorated with [ServiceClient");
    }

    [Fact]
    public void Create_InterfaceWithEmptyAppId_ThrowsInvalidOperationException()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            ServiceProxyFactory.Create<IEmptyAppIdService>(_invoker));

        ex.Message.ShouldContain("must be decorated with [ServiceClient");
    }

    [Fact]
    public void Create_ValidInterface_Succeeds()
    {
        var proxy = ServiceProxyFactory.Create<IValidService>(_invoker);
        proxy.ShouldNotBeNull();
    }
}
