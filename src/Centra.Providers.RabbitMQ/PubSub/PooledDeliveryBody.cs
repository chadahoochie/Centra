using System.Buffers;

namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// An owned copy of a RabbitMQ delivery body, taken from <see cref="ArrayPool{T}.Shared"/>.
/// </summary>
/// <remarks>
/// The client only guarantees the memory behind <c>BasicDeliverEventArgs.Body</c> until the
/// <c>ReceivedAsync</c> callback returns, after which it may recycle that buffer for the next
/// delivery. Any path that defers the read past the callback - the concurrent dispatch path, whose
/// continuations a shutdown drain now keeps alive to completion - must therefore read a copy it
/// owns. Pooled rather than freshly allocated so the per-message path stays allocation free, which
/// makes <see cref="Return"/> mandatory: call it exactly once, from the <c>finally</c> of the
/// continuation that consumed <see cref="Memory"/>, and never touch <see cref="Memory"/> afterwards.
/// </remarks>
internal readonly struct PooledDeliveryBody
{
    private readonly byte[]? _buffer;
    private readonly int _length;

    /// <summary>
    /// Copies <paramref name="source"/> into a rented buffer. An empty body rents nothing.
    /// </summary>
    public PooledDeliveryBody(ReadOnlyMemory<byte> source)
    {
        _length = source.Length;
        if (_length == 0)
        {
            _buffer = null;
            return;
        }

        _buffer = ArrayPool<byte>.Shared.Rent(_length);
        source.Span.CopyTo(_buffer);
    }

    /// <summary>
    /// The copied body. Valid only until <see cref="Return"/> is called.
    /// </summary>
    public ReadOnlyMemory<byte> Memory => _buffer is null
        ? ReadOnlyMemory<byte>.Empty
        : new ReadOnlyMemory<byte>(_buffer, 0, _length);

    /// <summary>
    /// Releases the rented buffer back to the pool. Safe on an empty body; not safe to call twice.
    /// </summary>
    public void Return()
    {
        if (_buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }
}
