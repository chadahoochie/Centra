using Centra.Core.Actors;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorMailboxTests
{
    [Fact]
    public async Task Should_Execute_Turns_Sequentially_In_FIFO_Order()
    {
        var mailbox = new ActorMailbox();
        var sequence = new List<int>();

        var task1 = mailbox.EnqueueTurnAsync(async () =>
        {
            await Task.Yield();
            sequence.Add(1);
        });

        var task2 = mailbox.EnqueueTurnAsync(async () =>
        {
            await Task.Yield();
            sequence.Add(2);
        });

        var task3 = mailbox.EnqueueTurnAsync(() =>
        {
            sequence.Add(3);
            return ValueTask.CompletedTask;
        });

        await Task.WhenAll(task1.AsTask(), task2.AsTask(), task3.AsTask());

        sequence.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Should_Return_Result_From_Typed_Turn()
    {
        var mailbox = new ActorMailbox();

        var result = await mailbox.EnqueueTurnAsync(async () =>
        {
            await Task.Yield();
            return 42;
        });

        result.ShouldBe(42);
    }

    [Fact]
    public async Task Should_Guarantee_Single_Threaded_Turn_Execution_Under_Concurrency_Stress()
    {
        var mailbox = new ActorMailbox();
        var counter = 0; // Deliberately NOT volatile, NOT Interlocked
        var concurrencyDetected = false;
        var activeTurnCount = 0;
        const int totalTurns = 100;

        var tasks = Enumerable.Range(0, totalTurns).Select(i => mailbox.EnqueueTurnAsync(async () =>
        {
            var current = Interlocked.Increment(ref activeTurnCount);
            if (current > 1)
            {
                concurrencyDetected = true;
            }

            // Asynchronous yield to simulate non-blocking turn execution
            await Task.Yield();
            counter++;

            Interlocked.Decrement(ref activeTurnCount);
        })).Select(vt => vt.AsTask()).ToArray();

        await Task.WhenAll(tasks);

        concurrencyDetected.ShouldBeFalse();
        counter.ShouldBe(totalTurns);
    }

    [Fact]
    public async Task Should_Propagate_Exception_And_Continue_Processing_Subsequent_Turns()
    {
        var mailbox = new ActorMailbox();

        var failedTask = mailbox.EnqueueTurnAsync(() => throw new InvalidOperationException("Turn failed!"));
        var successfulTask = mailbox.EnqueueTurnAsync(() => ValueTask.FromResult("recovered"));

        await Should.ThrowAsync<InvalidOperationException>(failedTask.AsTask);

        var result = await successfulTask;
        result.ShouldBe("recovered");
    }

    [Fact]
    public async Task Should_Honor_Cancellation_Token_Before_Turn_Starts()
    {
        var mailbox = new ActorMailbox();
        using var cts = new CancellationTokenSource();

        // Enqueue long-running first turn
        var firstTurnStarted = new TaskCompletionSource();
        var releaseFirstTurn = new TaskCompletionSource();

        var task1 = mailbox.EnqueueTurnAsync(async () =>
        {
            firstTurnStarted.SetResult();
            await releaseFirstTurn.Task;
        });

        await firstTurnStarted.Task;

        // Cancel token before task 2 executes
        cts.Cancel();
        var task2 = mailbox.EnqueueTurnAsync(() => ValueTask.CompletedTask, cts.Token);

        releaseFirstTurn.SetResult();
        await task1;

        await Should.ThrowAsync<OperationCanceledException>(task2.AsTask);
    }

    [Fact]
    public async Task Should_Drain_Pending_Turns_Gracefully()
    {
        var mailbox = new ActorMailbox();
        var completed = 0;

        for (var i = 0; i < 5; i++)
        {
            _ = mailbox.EnqueueTurnAsync(async () =>
            {
                await Task.Yield();
                Interlocked.Increment(ref completed);
            });
        }

        var drained = await mailbox.DrainAsync(TimeSpan.FromSeconds(2));

        drained.ShouldBeTrue();
        completed.ShouldBe(5);
    }

    [Fact]
    public async Task Should_Throw_ObjectDisposedException_When_Enqueuing_To_Disposed_Mailbox()
    {
        var mailbox = new ActorMailbox();
        await mailbox.DisposeAsync();

        Should.Throw<ObjectDisposedException>(() => mailbox.EnqueueTurnAsync(() => ValueTask.CompletedTask));
        Should.Throw<ObjectDisposedException>(() => mailbox.EnqueueTurnAsync(() => ValueTask.FromResult(1)));
    }

    [Fact]
    public void Should_Return_Canceled_ValueTask_When_PreCanceled_Token_Supplied()
    {
        var mailbox = new ActorMailbox();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var vt = mailbox.EnqueueTurnAsync(() => ValueTask.CompletedTask, cts.Token);
        vt.IsCanceled.ShouldBeTrue();

        var vtTyped = mailbox.EnqueueTurnAsync(() => ValueTask.FromResult(42), cts.Token);
        vtTyped.IsCanceled.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Return_False_When_Drain_Times_Out()
    {
        var mailbox = new ActorMailbox();
        var blockedTurn = new TaskCompletionSource();

        _ = mailbox.EnqueueTurnAsync(async () => await blockedTurn.Task);

        var drained = await mailbox.DrainAsync(TimeSpan.FromMilliseconds(50));
        drained.ShouldBeFalse();

        blockedTurn.SetResult();
        await mailbox.DisposeAsync();
    }

    [Fact]
    public async Task Should_Handle_Double_Disposal_Safely()
    {
        var mailbox = new ActorMailbox();
        await mailbox.DisposeAsync();
        await mailbox.DisposeAsync();
    }
}
