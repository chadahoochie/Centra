using Centra.Core.Workflows;
using Centra.Serialization;
using Centra.State;
using Centra.Workflows;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Centra.Tests.Unit.Workflows;

internal static class DurableWorkflowTestEngineFactory
{
    public static (IWorkflowEngine Engine, IWorkflowRegistry Registry, IWorkflowHistoryStore HistoryStore) Create(
        IStateStore stateStore,
        Dictionary<string, object> stateDatabase,
        IDurableWorkflowTimerStore timerStore,
        ICentraSerializer serializer,
        FakeTimeProvider timeProvider,
        IServiceProvider serviceProvider)
    {
        stateStore.GetAsync<WorkflowStateRecord>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<StateOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(1);
                if (stateDatabase.TryGetValue(key, out var val) && val is WorkflowStateRecord record)
                {
                    return new StateEntry<WorkflowStateRecord>(key, record, "etag-1");
                }
                return null;
            });

        stateStore.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkflowStateRecord>(), Arg.Any<StateOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(1);
                var record = callInfo.Arg<WorkflowStateRecord>();
                stateDatabase[key] = record;
                return ValueTask.CompletedTask;
            });

        stateStore.GetAsync<WorkflowHistoryRecord>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<StateOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(1);
                if (stateDatabase.TryGetValue(key, out var val) && val is WorkflowHistoryRecord record)
                {
                    return new StateEntry<WorkflowHistoryRecord>(key, record, "etag-hist-1");
                }
                return null;
            });

        stateStore.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkflowHistoryRecord>(), Arg.Any<StateOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(1);
                var record = callInfo.Arg<WorkflowHistoryRecord>();
                stateDatabase[key] = record;
                return ValueTask.CompletedTask;
            });

        var registry = new WorkflowRegistry();
        var historyStore = new WorkflowHistoryStore(stateStore, "statestore");
        var dispatcher = Substitute.For<IWorkflowActivityDispatcher>();

        var engine = new WorkflowEngine(
            serviceProvider,
            registry,
            historyStore,
            dispatcher,
            serializer,
            timeProvider,
            durableTimerStore: timerStore);

        return (engine, registry, historyStore);
    }
}
