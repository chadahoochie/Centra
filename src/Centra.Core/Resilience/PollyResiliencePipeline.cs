using Centra.Resilience;
using Polly;

namespace Centra.Resilience;

/// <summary>
/// High-performance implementation of <see cref="IResiliencePipeline"/> backed by Polly v8 Core.
/// </summary>
public sealed class PollyResiliencePipeline : IResiliencePipeline
{
    private readonly ResiliencePipeline _pipeline;

    public PollyResiliencePipeline(ResiliencePipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(
        Func<CancellationToken, ValueTask> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);

        await _pipeline.ExecuteAsync(
            static async (state, ct) => await state(ct).ConfigureAwait(false),
            callback,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return await _pipeline.ExecuteAsync(
            static async (state, ct) => await state(ct).ConfigureAwait(false),
            callback,
            cancellationToken).ConfigureAwait(false);
    }
}
