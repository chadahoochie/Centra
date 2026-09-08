using System;
using System.Threading;
using System.Threading.Tasks;
using Centra.Core.Actors;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorTurnTests
{
    public sealed class ActorActionTurnTests
    {
        [Fact]
        public async Task Should_CompleteTask_When_ActionExecutesSuccessfully()
        {
            // Arrange
            var actionExecuted = false;
            Func<ValueTask> action = () =>
            {
                actionExecuted = true;
                return default;
            };
            var sut = new ActorActionTurn(action, CancellationToken.None);

            // Act
            await sut.ExecuteAsync();

            // Assert
            actionExecuted.ShouldBeTrue();
            sut.Task.IsCompletedSuccessfully.ShouldBeTrue();
        }

        [Fact]
        public async Task Should_CancelTask_When_CancellationTokenAlreadyCancelled()
        {
            // Arrange
            var actionExecuted = false;
            Func<ValueTask> action = () =>
            {
                actionExecuted = true;
                return default;
            };
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var sut = new ActorActionTurn(action, cts.Token);

            // Act
            await sut.ExecuteAsync();

            // Assert
            actionExecuted.ShouldBeFalse();
            sut.Task.IsCanceled.ShouldBeTrue();
        }

        [Fact]
        public async Task Should_CancelTask_When_ActionThrowsMatchingOperationCanceledException()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            Func<ValueTask> action = () => throw new OperationCanceledException(cts.Token);
            var sut = new ActorActionTurn(action, cts.Token);

            // Act
            await sut.ExecuteAsync();

            // Assert
            sut.Task.IsCanceled.ShouldBeTrue();
        }

        [Fact]
        public async Task Should_FaultTask_When_ActionThrowsArbitraryException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Test exception");
            Func<ValueTask> action = () => throw expectedException;
            var sut = new ActorActionTurn(action, CancellationToken.None);

            // Act
            await sut.ExecuteAsync();

            // Assert
            sut.Task.IsFaulted.ShouldBeTrue();
            var exception = await Should.ThrowAsync<InvalidOperationException>(() => sut.Task);
            exception.ShouldBe(expectedException);
        }

        [Fact]
        public void Should_CancelTask_When_SetCanceledIsCalled()
        {
            // Arrange
            Func<ValueTask> action = () => default;
            using var cts = new CancellationTokenSource();
            var sut = new ActorActionTurn(action, cts.Token);

            // Act
            sut.SetCanceled();

            // Assert
            sut.Task.IsCanceled.ShouldBeTrue();
        }
    }

    public sealed class ActorFunctionTurnTests
    {
        [Fact]
        public async Task Should_CompleteTaskWithResult_When_ActionExecutesSuccessfully()
        {
            // Arrange
            var expectedResult = "success";
            Func<ValueTask<string>> action = () => new ValueTask<string>(expectedResult);
            var sut = new ActorFunctionTurn<string>(action, CancellationToken.None);

            // Act
            await sut.ExecuteAsync();

            // Assert
            sut.Task.IsCompletedSuccessfully.ShouldBeTrue();
            var result = await sut.Task;
            result.ShouldBe(expectedResult);
        }

        [Fact]
        public async Task Should_CancelTask_When_CancellationTokenAlreadyCancelled()
        {
            // Arrange
            var actionExecuted = false;
            Func<ValueTask<string>> action = () =>
            {
                actionExecuted = true;
                return new ValueTask<string>("test");
            };
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var sut = new ActorFunctionTurn<string>(action, cts.Token);

            // Act
            await sut.ExecuteAsync();

            // Assert
            actionExecuted.ShouldBeFalse();
            sut.Task.IsCanceled.ShouldBeTrue();
        }

        [Fact]
        public async Task Should_CancelTask_When_ActionThrowsMatchingOperationCanceledException()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            Func<ValueTask<string>> action = () => throw new OperationCanceledException(cts.Token);
            var sut = new ActorFunctionTurn<string>(action, cts.Token);

            // Act
            await sut.ExecuteAsync();

            // Assert
            sut.Task.IsCanceled.ShouldBeTrue();
        }

        [Fact]
        public async Task Should_FaultTask_When_ActionThrowsArbitraryException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Test exception");
            Func<ValueTask<string>> action = () => throw expectedException;
            var sut = new ActorFunctionTurn<string>(action, CancellationToken.None);

            // Act
            await sut.ExecuteAsync();

            // Assert
            sut.Task.IsFaulted.ShouldBeTrue();
            var exception = await Should.ThrowAsync<InvalidOperationException>(() => sut.Task);
            exception.ShouldBe(expectedException);
        }

        [Fact]
        public void Should_CancelTask_When_SetCanceledIsCalled()
        {
            // Arrange
            Func<ValueTask<string>> action = () => new ValueTask<string>("test");
            using var cts = new CancellationTokenSource();
            var sut = new ActorFunctionTurn<string>(action, cts.Token);

            // Act
            sut.SetCanceled();

            // Assert
            sut.Task.IsCanceled.ShouldBeTrue();
        }
    }
}
