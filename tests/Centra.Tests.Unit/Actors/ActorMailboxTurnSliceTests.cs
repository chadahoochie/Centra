using Centra.Core.Actors;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Actors;

public sealed class ActorMailboxTurnSliceTests
{
    [Fact]
    public async Task Should_Process_Turns_Across_Multiple_Slices_Correctly()
    {
        // Arrange: small slice of 3 turns
        var mailbox = new ActorMailbox(maxTurnsPerSlice: 3);
        const int totalTurns = 12;
        var executedTurns = new List<int>();

        // Act: enqueue 12 turns
        var tasks = new Task[totalTurns];
        for (int i = 0; i < totalTurns; i++)
        {
            var turnId = i;
            tasks[i] = mailbox.EnqueueTurnAsync(async () =>
            {
                await Task.Yield();
                lock (executedTurns)
                {
                    executedTurns.Add(turnId);
                }
            }).AsTask();
        }

        await Task.WhenAll(tasks);

        // Assert
        executedTurns.Count.ShouldBe(totalTurns);
        for (int i = 0; i < totalTurns; i++)
        {
            executedTurns[i].ShouldBe(i);
        }
    }

    [Fact]
    public async Task Should_Reactivate_From_Idle_When_New_Turns_Are_Enqueued_Later()
    {
        // Arrange
        var mailbox = new ActorMailbox(maxTurnsPerSlice: 5);

        // First burst: 3 turns
        var result1 = await mailbox.EnqueueTurnAsync(() => ValueTask.FromResult(10));
        var result2 = await mailbox.EnqueueTurnAsync(() => ValueTask.FromResult(20));

        result1.ShouldBe(10);
        result2.ShouldBe(20);

        // Give worker time to settle into StatusIdle
        await Task.Delay(50);

        // Second burst: enqueue into idle mailbox
        var result3 = await mailbox.EnqueueTurnAsync(async () =>
        {
            await Task.Yield();
            return 30;
        });

        result3.ShouldBe(30);
    }

    [Fact]
    public async Task Should_Maintain_Strict_Sequential_Execution_Under_High_Concurrency_And_Turn_Slicing()
    {
        // Arrange
        var mailbox = new ActorMailbox(maxTurnsPerSlice: 5);
        const int totalTurns = 500;
        int activeTurnCount = 0;
        bool concurrencyDetected = false;
        int executionCounter = 0;

        // Act: enqueue 500 turns across threadpool tasks concurrently
        var enqueueTasks = Enumerable.Range(0, 50).Select(batch => Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                await mailbox.EnqueueTurnAsync(async () =>
                {
                    var current = Interlocked.Increment(ref activeTurnCount);
                    if (current > 1)
                    {
                        concurrencyDetected = true;
                    }

                    await Task.Yield();
                    executionCounter++;
                    Interlocked.Decrement(ref activeTurnCount);
                });
            }
        })).ToArray();

        await Task.WhenAll(enqueueTasks);

        // Assert
        concurrencyDetected.ShouldBeFalse();
        executionCounter.ShouldBe(totalTurns);
    }

    [Fact]
    public async Task Should_Drain_Safely_When_Turns_Are_Being_Enqueued_Concurrently()
    {
        // Arrange
        var mailbox = new ActorMailbox(maxTurnsPerSlice: 5);
        int completedTurns = 0;

        for (int i = 0; i < 20; i++)
        {
            _ = mailbox.EnqueueTurnAsync(async () =>
            {
                await Task.Yield();
                Interlocked.Increment(ref completedTurns);
            });
        }

        // Act
        var drained = await mailbox.DrainAsync(TimeSpan.FromSeconds(3));

        // Assert
        drained.ShouldBeTrue();
        completedTurns.ShouldBe(20);
    }
}
