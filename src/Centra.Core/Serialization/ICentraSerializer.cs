using System.Buffers;

namespace Centra.Serialization;

public interface ICentraSerializer
{
    void Serialize<T>(T value, IBufferWriter<byte> writer);
    byte[] Serialize<T>(T value);
    byte[] Serialize(object? value, Type inputType);
    T? Deserialize<T>(ReadOnlyMemory<byte> buffer);
    object? Deserialize(ReadOnlyMemory<byte> buffer, Type returnType);
}
