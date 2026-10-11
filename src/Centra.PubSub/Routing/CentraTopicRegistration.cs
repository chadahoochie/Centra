using System.Reflection;
using Centra.Events;
using Centra.PubSub;
using Centra.Serialization;

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
    public Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, ICentraSerializer?, CancellationToken, Task<EventHandlingResult>>? SerializerAwareInvoker { get; init; }
    public Type? SerializerType { get; init; }
    public ICentraSerializer? Serializer { get; init; }
    public string? ContentType { get; init; }

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
        IReadOnlyDictionary<string, object?>? customArguments = null,
        Type? serializerType = null,
        ICentraSerializer? serializer = null,
        string? contentType = null,
        Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, ICentraSerializer?, CancellationToken, Task<EventHandlingResult>>? serializerAwareInvoker = null)
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
            customArguments: customArguments,
            serializerType: serializerType,
            serializer: serializer,
            contentType: contentType,
            serializerAwareInvoker: serializerAwareInvoker)
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
        IReadOnlyDictionary<string, object?>? customArguments = null,
        Type? serializerType = null,
        ICentraSerializer? serializer = null,
        string? contentType = null,
        Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, ICentraSerializer?, CancellationToken, Task<EventHandlingResult>>? serializerAwareInvoker = null)
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
        SerializerType = serializerType;
        Serializer = serializer;
        ContentType = contentType;

        if (serializerAwareInvoker != null)
        {
            SerializerAwareInvoker = serializerAwareInvoker;
            Invoker = invoker ?? ((h, p, hdrs, ct) => serializerAwareInvoker(h, p, hdrs, null, ct));
        }
        else if (invoker != null)
        {
            Invoker = invoker;
            SerializerAwareInvoker = (h, p, hdrs, s, ct) => invoker(h, p, hdrs, ct);
        }
        else
        {
            var typedInvoker = CreateInvoker(eventType);
            SerializerAwareInvoker = typedInvoker;
            Invoker = (h, p, hdrs, ct) => typedInvoker(h, p, hdrs, null, ct);
        }
    }

    public Task<EventHandlingResult> InvokeAsync(
        object handler,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> headers,
        ICentraSerializer? serializer,
        CancellationToken cancellationToken)
    {
        if (SerializerAwareInvoker != null)
        {
            return SerializerAwareInvoker(handler, payload, headers, serializer, cancellationToken);
        }

        return Invoker(handler, payload, headers, cancellationToken);
    }

    internal static Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, ICentraSerializer?, CancellationToken, Task<EventHandlingResult>> CreateInvoker(Type eventType)
    {
        var genericMethod = CreateTypedInvokerMethod.MakeGenericMethod(eventType);
        return (Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, ICentraSerializer?, CancellationToken, Task<EventHandlingResult>>)genericMethod.Invoke(null, null)!;
    }

    internal static Func<object, ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, ICentraSerializer?, CancellationToken, Task<EventHandlingResult>> CreateTypedInvoker<TEvent>()
    {
        return static async (handlerObj, payload, headers, serializer, ct) =>
        {
            var handler = (IEventHandler<TEvent>)handlerObj;
            var unpacked = CloudEventUnpacker.Unpack<TEvent>(payload, headers, serializer);
            if (unpacked.Data is null)
            {
                return EventHandlingResult.Drop;
            }

            return await handler.HandleAsync(unpacked.Data, unpacked.Context, ct).ConfigureAwait(false);
        };
    }
}
