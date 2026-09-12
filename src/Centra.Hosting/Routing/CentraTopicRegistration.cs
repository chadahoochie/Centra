using System.Reflection;
using Centra.Events;
using Centra.PubSub;

namespace Centra.Hosting.Routing;

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
