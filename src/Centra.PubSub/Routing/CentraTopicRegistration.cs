using System.Reflection;
using Centra.Events;
using Centra.PubSub;

namespace Centra.PubSub.Routing;

public sealed record CentraTopicRegistration
{
    private static readonly MethodInfo CreateTypedInvokerMethod =
        typeof(CentraTopicRegistration).GetMethod(nameof(CreateTypedInvoker), BindingFlags.NonPublic | BindingFlags.Static)!;

    public string PubSubName { get; init; }
    public string Topic { get; init; }
    public Type EventType { get; init; }
    public Type HandlerType { get; init; }
    public string? DeadLetterTopic { get; init; }
    public ConsumerMode ConsumerMode { get; init; }
    public int? PrefetchCount { get; init; }
    public int? MaxConcurrentCalls { get; init; }
    public TimeSpan? MessageTimeToLive { get; init; }
    public bool AutoDelete { get; init; }
    public IReadOnlyDictionary<string, object?>? CustomArguments { get; init; }
    public string? RuleFilter { get; init; }
    public int Priority { get; init; }
    public ICompiledRuleFilter? CompiledFilter { get; init; }
    public Func<EventContext, ReadOnlyMemory<byte>, bool>? Predicate { get; init; }
    public Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, Task<EventHandlingResult>> Invoker { get; init; }

    public CentraTopicRegistration(
        string pubSubName,
        string topic,
        Type eventType,
        Type handlerType,
        string? deadLetterTopic = null,
        Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, Task<EventHandlingResult>>? invoker = null,
        ConsumerMode consumerMode = ConsumerMode.CompetingConsumer,
        int? prefetchCount = null,
        int? maxConcurrentCalls = null,
        TimeSpan? messageTimeToLive = null,
        bool autoDelete = false,
        IReadOnlyDictionary<string, object?>? customArguments = null)
        : this(
            pubSubName,
            topic,
            eventType,
            handlerType,
            deadLetterTopic,
            invoker,
            ruleFilter: null,
            priority: 0,
            compiledFilter: null,
            predicate: null,
            consumerMode: consumerMode,
            prefetchCount: prefetchCount,
            maxConcurrentCalls: maxConcurrentCalls,
            messageTimeToLive: messageTimeToLive,
            autoDelete: autoDelete,
            customArguments: customArguments)
    {
    }

    public CentraTopicRegistration(
        string pubSubName,
        string topic,
        Type eventType,
        Type handlerType,
        string? deadLetterTopic,
        Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, Task<EventHandlingResult>>? invoker,
        string? ruleFilter,
        int priority,
        ICompiledRuleFilter? compiledFilter = null,
        Func<EventContext, ReadOnlyMemory<byte>, bool>? predicate = null,
        ConsumerMode consumerMode = ConsumerMode.CompetingConsumer,
        int? prefetchCount = null,
        int? maxConcurrentCalls = null,
        TimeSpan? messageTimeToLive = null,
        bool autoDelete = false,
        IReadOnlyDictionary<string, object?>? customArguments = null)
    {
        PubSubName = pubSubName;
        Topic = topic;
        EventType = eventType;
        HandlerType = handlerType;
        DeadLetterTopic = deadLetterTopic;
        ConsumerMode = consumerMode;
        PrefetchCount = prefetchCount;
        MaxConcurrentCalls = maxConcurrentCalls;
        MessageTimeToLive = messageTimeToLive;
        AutoDelete = autoDelete;
        CustomArguments = customArguments;
        RuleFilter = ruleFilter;
        Priority = priority;
        CompiledFilter = compiledFilter;
        Predicate = predicate;
        Invoker = invoker ?? CreateInvoker(eventType);
    }

    internal static Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, Task<EventHandlingResult>> CreateInvoker(Type eventType)
    {
        var genericMethod = CreateTypedInvokerMethod.MakeGenericMethod(eventType);
        return (Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, Task<EventHandlingResult>>)genericMethod.Invoke(null, null)!;
    }

    internal static Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, Task<EventHandlingResult>> CreateTypedInvoker<TEvent>()
    {
        return static async (handlerObj, payload, headers, ct) =>
        {
            var handler = (IEventHandler<TEvent>)handlerObj;
            var unpacked = CloudEventUnpacker.Unpack<TEvent>(payload, headers);
            if (unpacked.Data is null)
            {
                return EventHandlingResult.Drop;
            }

            return await handler.HandleAsync(unpacked.Data, unpacked.Context, ct).ConfigureAwait(false);
        };
    }
}
