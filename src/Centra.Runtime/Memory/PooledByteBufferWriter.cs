using System.Buffers;

namespace Centra.Memory;

public sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _rentedBuffer;
    private int _index;
    private const int DefaultInitialCapacity = 256;

    public PooledByteBufferWriter(int initialCapacity = DefaultInitialCapacity)
    {
        _rentedBuffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _index = 0;
    }

    public ReadOnlyMemory<byte> WrittenMemory => _rentedBuffer.AsMemory(0, _index);
    public ReadOnlySpan<byte> WrittenSpan => _rentedBuffer.AsSpan(0, _index);
    public int WrittenCount => _index;
    public int Capacity => _rentedBuffer.Length;
    public int FreeCapacity => _rentedBuffer.Length - _index;

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_index > _rentedBuffer.Length - count)
        {
            throw new InvalidOperationException("Cannot advance beyond buffer length");
        }

        _index += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _rentedBuffer.AsMemory(_index);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _rentedBuffer.AsSpan(_index);
    }

    public void Reset()
    {
        _index = 0;
    }

    public byte[] ToArray()
    {
        return WrittenSpan.ToArray();
    }

    private void CheckAndResizeBuffer(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint > FreeCapacity)
        {
            var growBy = Math.Max(sizeHint, _rentedBuffer.Length);
            var newSize = checked(_rentedBuffer.Length + growBy);

            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            _rentedBuffer.AsSpan(0, _index).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = newBuffer;
        }
    }

    public void Dispose()
    {
        if (_rentedBuffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = [];
            _index = 0;
        }
    }
}
