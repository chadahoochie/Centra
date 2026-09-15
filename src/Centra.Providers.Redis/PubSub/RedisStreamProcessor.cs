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

    public const string EnvelopeField = "envelope";

    public async ValueTask PublishToStreamAsync(
        IDatabase db,
        string streamKey,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamKey);

        var framedBytes = RedisMessagePayloadCodec.Encode(payload, metadata);
        await db.StreamAddAsync(streamKey, EnvelopeField, framedBytes).ConfigureAwait(false);
    }

    public async ValueTask PublishBatchToStreamAsync(
        IDatabase db,
        string streamKey,
        IReadOnlyList<PubSubMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamKey);
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            return;
        }

        var batch = db.CreateBatch();
        var tasks = new Task[messages.Count];
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            var framedBytes = RedisMessagePayloadCodec.Encode(msg.Payload, msg.Metadata);
            tasks[i] = batch.StreamAddAsync(streamKey, EnvelopeField, framedBytes);
        }

        batch.Execute();
        await Task.WhenAll(tasks).ConfigureAwait(false);
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

                var ackIds = new List<RedisValue>(entries.Length);
                try
                {
                    for (int i = 0; i < entries.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = entries[i];
                        await ProcessStreamEntryAsync(db, streamKey, groupName, pubSubName, deadLetterTopic, handler, entry, logger, cancellationToken, autoAcknowledge: false).ConfigureAwait(false);
                        ackIds.Add(entry.Id);
                    }
                }
                finally
                {
                    if (ackIds.Count > 0)
                    {
                        await db.StreamAcknowledgeAsync(streamKey, groupName, ackIds.ToArray()).ConfigureAwait(false);
                    }
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
        CancellationToken cancellationToken,
        bool autoAcknowledge = true)
    {
        try
        {
            ReadOnlyMemory<byte> payload = ReadOnlyMemory<byte>.Empty;
            IReadOnlyDictionary<string, string> headers = new Dictionary<string, string>();

            var envelopeField = entry.Values.FirstOrDefault(v => v.Name == EnvelopeField);
            if (!envelopeField.Value.IsNullOrEmpty)
            {
                byte[] raw = (byte[])envelopeField.Value!;
                RedisMessagePayloadCodec.TryDecode(raw, out payload, out headers);
            }
            else
            {
                var payloadField = entry.Values.FirstOrDefault(v => v.Name == "payload");
                if (!payloadField.Value.IsNullOrEmpty)
                {
                    payload = (byte[])payloadField.Value!;
                    var metadataField = entry.Values.FirstOrDefault(v => v.Name == "metadata" || v.Name == "headers");
                    if (!metadataField.Value.IsNullOrEmpty)
                    {
                        headers = JsonSerializer.Deserialize<Dictionary<string, string>>((byte[])metadataField.Value!)
                            ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
                    }
                }
            }

            var result = await handler(payload, headers, cancellationToken).ConfigureAwait(false);

            if (result == EventHandlingResult.DeadLetter && !string.IsNullOrWhiteSpace(deadLetterTopic))
            {
                await PublishToStreamAsync(db, deadLetterTopic, payload, headers, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Redis stream entry {EntryId} on {Stream}", entry.Id, streamKey);
        }
        finally
        {
            if (autoAcknowledge)
            {
                await db.StreamAcknowledgeAsync(streamKey, groupName, entry.Id).ConfigureAwait(false);
            }
        }
    }
}
