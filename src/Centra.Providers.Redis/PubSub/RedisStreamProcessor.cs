using System.Text.Json;
using Centra.Locks;
using Centra.PubSub;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Centra.Providers.Redis.PubSub;

/// <summary>
/// Default implementation of <see cref="IRedisStreamProcessor"/>.
/// </summary>
public sealed class RedisStreamProcessor : IRedisStreamProcessor
{
    /// <summary>
    /// Singleton default instance of <see cref="RedisStreamProcessor"/>.
    /// </summary>
    public static readonly RedisStreamProcessor Instance = new();

    private const string EnvelopeField = "envelope";

    public async ValueTask PublishToStreamAsync(
        IDatabase db,
        string streamKey,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamKey);

        var envelope = new RedisMessageEnvelope(metadata, payload.ToArray());
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);

        await db.StreamAddAsync(streamKey, EnvelopeField, envelopeBytes).ConfigureAwait(false);
    }

    public async Task RunStreamLoopAsync(
        IDatabase db,
        IDistributedLockProvider? lockProvider,
        string streamKey,
        string groupName,
        string consumerName,
        string pubSubName,
        string? deadLetterTopic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        ConsumerMode consumerMode,
        ILogger logger,
        CancellationToken cancellationToken,
        int batchSize = 10)
    {
        IDistributedLock? heldLock = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (consumerMode == ConsumerMode.SingleActiveConsumer && lockProvider is not null && heldLock is null)
                {
                    heldLock = await lockProvider.TryAcquireLockAsync(
                        $"{pubSubName}-pubsub-lock",
                        $"{streamKey}:single-active",
                        TimeSpan.FromSeconds(30),
                        cancellationToken).ConfigureAwait(false);

                    if (heldLock is null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                StreamEntry[] entries;
                try
                {
                    entries = await db.StreamReadGroupAsync(streamKey, groupName, consumerName, StreamPosition.NewMessages, count: batchSize).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error reading Redis stream {Stream}", streamKey);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (entries.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var entry in entries)
                {
                    await ProcessStreamEntryAsync(db, streamKey, groupName, pubSubName, deadLetterTopic, handler, entry, logger, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on unsubscribe/shutdown.
        }
        finally
        {
            if (heldLock is not null)
            {
                await heldLock.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async ValueTask ProcessStreamEntryAsync(
        IDatabase db,
        string streamKey,
        string groupName,
        string pubSubName,
        string? deadLetterTopic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        StreamEntry entry,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelopeField = entry.Values.FirstOrDefault(v => v.Name == EnvelopeField);
            byte[] raw = envelopeField.Value.IsNullOrEmpty ? Array.Empty<byte>() : (byte[])envelopeField.Value!;
            var envelope = raw.Length > 0 ? JsonSerializer.Deserialize<RedisMessageEnvelope>(raw) : null;

            var payload = (ReadOnlyMemory<byte>)(envelope?.Payload ?? Array.Empty<byte>());
            var headers = envelope?.Headers ?? new Dictionary<string, string>();

            var result = await handler(payload, headers, cancellationToken).ConfigureAwait(false);

            if (result == EventHandlingResult.DeadLetter && !string.IsNullOrWhiteSpace(deadLetterTopic))
            {
                await PublishToStreamAsync(db, deadLetterTopic, payload, headers, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Redis stream entry {EntryId} on {Stream}", entry.Id, streamKey);
        }
        finally
        {
            await db.StreamAcknowledgeAsync(streamKey, groupName, entry.Id).ConfigureAwait(false);
        }
    }
}
