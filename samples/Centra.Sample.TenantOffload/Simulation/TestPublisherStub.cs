using Centra.PubSub;

namespace Centra.Sample.TenantOffload.Simulation;

public sealed class TestPublisherStub : IPubSubPublisher
{
    public ValueTask PublishAsync(
        string pubSubName,
        string topic,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
