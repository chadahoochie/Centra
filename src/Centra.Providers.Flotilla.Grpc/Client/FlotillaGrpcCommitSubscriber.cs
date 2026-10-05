using Centra.Providers.Flotilla.Client;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Centra.Providers.Flotilla.Grpc.Client;

internal sealed class FlotillaGrpcCommitSubscriber : IAsyncDisposable
{
    private readonly FlotillaService.FlotillaServiceClient _client;
    private readonly FlotillaCommitChannel _commitChannel;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _streamTask;

    public FlotillaGrpcCommitSubscriber(
        FlotillaService.FlotillaServiceClient client,
        FlotillaCommitChannel commitChannel,
        ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _commitChannel = commitChannel ?? throw new ArgumentNullException(nameof(commitChannel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start()
    {
        _streamTask = Task.Run(RunStreamAsync);
    }

    internal async Task RunStreamAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var call = _client.SubscribeCommits(new CommitSubscribeRequest { FromIndex = 0 }, cancellationToken: _cts.Token);
                while (await call.ResponseStream.MoveNext(_cts.Token).ConfigureAwait(false))
                {
                    var proto = call.ResponseStream.Current;
                    var entry = new CommittedEntry
                    {
                        LogIndex = proto.Index,
                        Term = proto.Term,
                        Data = proto.Data.Memory
                    };
                    await _commitChannel.WriteCommitAsync(entry, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "gRPC commit subscription stream disconnected or unavailable; retrying...");
                try
                {
                    await Task.Delay(1000, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_streamTask is not null)
        {
            try
            {
                await _streamTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }
        _cts.Dispose();
    }
}
