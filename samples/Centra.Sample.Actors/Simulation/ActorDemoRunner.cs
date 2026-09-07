using System.Diagnostics;
using Centra.Actors;
using Centra.Core.Actors;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Centra.Sample.Actors.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Centra.Sample.Actors.Simulation;

public static class ActorDemoRunner
{
    public static async Task<ActorSimulationResult> RunAsync(
        string[]? args = null,
        CancellationToken cancellationToken = default)
    {
        var logs = new List<string>();
        void Log(string message)
        {
            var line = $"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}";
            logs.Add(line);
            Console.WriteLine(line);
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine(" CENTRA DISTRIBUTED APPLICATION FRAMEWORK - VIRTUAL ACTORS RUNTIME ENGINE       ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        var stopwatch = Stopwatch.StartNew();

        Log("Building host with Centra Virtual Actors...");
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddCentra(options =>
                {
                    options.AppId = "actor-demo-service";
                    options.DefaultStateStore = "statestore";
                    options.DefaultLockStore = "lockstore";
                });
                services.AddCentraInMemory();
                services.AddCentraActors(options =>
                {
                    options.DefaultStateStore = "statestore";
                    options.DefaultLockStore = "lockstore";
                    options.ActorIdleTimeout = TimeSpan.FromSeconds(5);
                    options.ReminderInterval = TimeSpan.FromHours(1);
                });
                services.AddActor<AccountActor, IAccountActor>();
            })
            .Build();

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        var proxyFactory = host.Services.GetRequiredService<IActorProxyFactory>();
        var actorManager = host.Services.GetRequiredService<ActorManager>();
        var reminderCoordinator = host.Services.GetRequiredService<ActorReminderCoordinator>();

        // --- PHASE 1: VIRTUAL ACTOR ACTIVATION & TURN-BASED CONCURRENCY ---
        Console.ForegroundColor = ConsoleColor.Green;
        Log("\n--- PHASE 1: Turn-Based Concurrency & Virtual Actor Activation ---");
        Console.ResetColor();

        var accountId = new ActorId("acc-42");
        var accountProxy = proxyFactory.CreateActorProxy<IAccountActor>(accountId);

        const int concurrentDeposits = 50;
        const decimal depositAmount = 10m;
        Log($"Spawning {concurrentDeposits} concurrent deposit tasks of ${depositAmount} each to account '{accountId.Value}'...");

        var depositTasks = Enumerable.Range(0, concurrentDeposits)
            .Select(_ => accountProxy.DepositAsync(depositAmount).AsTask())
            .ToArray();

        await Task.WhenAll(depositTasks).ConfigureAwait(false);

        var balanceAfterDeposits = await accountProxy.GetBalanceAsync().ConfigureAwait(false);
        Log($"All {concurrentDeposits} concurrent deposit turns completed sequentially. Verified balance: ${balanceAfterDeposits:F2}");

        // --- PHASE 2: STATE PERSISTENCE ACROSS PASSIVATION & RE-ACTIVATION ---
        Console.ForegroundColor = ConsoleColor.Green;
        Log("\n--- PHASE 2: State Persistence Across Passivation & Re-Activation ---");
        Console.ResetColor();

        Log($"Current active actor instances in memory: {actorManager.ActiveCount}");
        var identity = new ActorIdentity(ActorType.FromType<AccountActor>(), accountId);
        var passivated = await actorManager.PassivateActorAsync(identity, cancellationToken).ConfigureAwait(false);
        Log($"Passivated actor '{identity}': {passivated}. Active instances in memory: {actorManager.ActiveCount}");

        Log("Invoking passivated actor via proxy with deposit of $50...");
        var balanceAfterPassivation = await accountProxy.DepositAsync(50m).ConfigureAwait(false);
        Log($"Actor automatically reactivated from persistent state store. New balance: ${balanceAfterPassivation:F2}. Active instances: {actorManager.ActiveCount}");

        var passivationSucceeded = passivated && balanceAfterPassivation == 550m;

        // --- PHASE 3: DURABLE REMINDERS & TIMER CALLBACKS ---
        Console.ForegroundColor = ConsoleColor.Green;
        Log("\n--- PHASE 3: Ephemeral Timers & Durable Reminders ---");
        Console.ResetColor();

        Log("Scheduling durable reminder 'monthly-interest' for actor...");
        await accountProxy.ScheduleInterestReminderAsync(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);

        // Passivate actor before reminder fires to prove durable reminder wakes up a dormant actor
        await actorManager.PassivateActorAsync(identity, cancellationToken).ConfigureAwait(false);
        Log($"Actor passivated before reminder fires. Active count: {actorManager.ActiveCount}");

        // Allow reminder coordinator to execute tick
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        var ticksExecuted = await reminderCoordinator.TickAsync(cancellationToken).ConfigureAwait(false);
        Log($"Reminder coordinator executed {ticksExecuted} due reminder(s). Dormant actor reactivated: {actorManager.ActiveCount > 0}");

        var balanceAfterInterest = await accountProxy.GetBalanceAsync().ConfigureAwait(false);
        Log($"Balance after reminder callback applied 5% interest: ${balanceAfterInterest:F2}");

        var reminderSucceeded = balanceAfterInterest == 577.50m;

        // --- PHASE 4: DYNAMIC PROXY & CLEAN TEARDOWN ---
        Console.ForegroundColor = ConsoleColor.Green;
        Log("\n--- PHASE 4: Dynamic Proxy & Clean Teardown ---");
        Console.ResetColor();

        var anotherProxy = proxyFactory.CreateActorProxy<IAccountActor>(new ActorId("acc-999"));
        var initialAnotherBalance = await anotherProxy.GetBalanceAsync().ConfigureAwait(false);
        Log($"Created dynamic proxy for separate account 'acc-999'. Default initial balance: ${initialAnotherBalance:F2}");

        stopwatch.Stop();
        Log($"\nActor runtime simulation completed in {stopwatch.ElapsedMilliseconds} ms.");

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
        host.Dispose();

        return new ActorSimulationResult(
            ActorId: accountId.Value,
            FinalBalance: balanceAfterInterest,
            ConcurrentDepositsCount: concurrentDeposits,
            PassivationAndReactivationSucceeded: passivationSucceeded,
            ReminderExecuted: reminderSucceeded,
            ElapsedDuration: stopwatch.Elapsed,
            LogEntries: logs);
    }
}
