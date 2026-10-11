using System.Buffers;
using Centra.Serialization;

namespace Centra.PubSub;

public sealed class PubSubSerializerAdapter : IPubSubSerializer
{
    private readonly ICentraSerializer _inner;

    public string ContentType => _inner.ContentType;

    public PubSubSerializerAdapter(ICentraSerializer inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void Serialize<T>(T value, IBufferWriter<byte> writer) => _inner.Serialize(value, writer);

    public byte[] Serialize<T>(T value) => _inner.Serialize(value);

    public byte[] Serialize(object? value, Type inputType) => _inner.Serialize(value, inputType);

    public T? Deserialize<T>(ReadOnlyMemory<byte> buffer) => _inner.Deserialize<T>(buffer);

    public object? Deserialize(ReadOnlyMemory<byte> buffer, Type returnType) => _inner.Deserialize(buffer, returnType);
}
