using Shouldly;
using Xunit;

namespace Centra.Generators.Tests.Unit;

public sealed class ActorProxyGeneratorTests
{
    [Fact]
    public void Should_Generate_Proxy_For_IActor_Interface()
    {
        // Arrange
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Centra.Actors;

            namespace SampleApp.Actors;

            public interface ICounterActor : IActor
            {
                Task<int> IncrementAsync(int amount, CancellationToken cancellationToken = default);
                Task ResetAsync(CancellationToken cancellationToken = default);
            }
            """;

        // Act
        var (diagnostics, generated) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.ShouldBeEmpty();
        generated.Length.ShouldBe(1);

        var proxySource = generated[0].SourceText;
        proxySource.ShouldContain("public sealed class CounterActorProxy : ICounterActor, global::Centra.Actors.IActorProxy");
        proxySource.ShouldContain("public global::Centra.Actors.ActorIdentity Identity => _identity;");
        proxySource.ShouldContain("public static ICounterActor Create(");
        proxySource.ShouldContain("if (_placementDirector.IsLocal(_identity))");
        proxySource.ShouldContain("await _actorManager.DispatchAsync(_identity, async actor =>");
        proxySource.ShouldContain("((ICounterActor)actor).IncrementAsync");
        proxySource.ShouldContain("((ICounterActor)actor).ResetAsync");
        proxySource.ShouldContain("_serviceInvoker.InvokeMethodAsync");
        proxySource.ShouldContain("CentraDiagnostics.StartActorInvokeActivity");
    }

    [Fact]
    public void Should_Not_Generate_For_Non_Actor_Interface()
    {
        // Arrange
        var source = """
            namespace SampleApp.Actors;

            public interface INotAnActor
            {
                int Calculate();
            }
            """;

        // Act
        var (diagnostics, generated) = GeneratorTestHelper.RunGenerator(source);

        // Assert
        diagnostics.ShouldBeEmpty();
        generated.ShouldBeEmpty();
    }
}
