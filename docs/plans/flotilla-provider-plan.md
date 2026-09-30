# Implementation Plan: Centra Flotilla Drop-In Provider (`Centra.Providers.Flotilla`)

## Executive Summary

Centra provides high-performance distributed application building blocks (.NET 10) with pluggable provider architectures across Pub/Sub, State, Locks, Bindings, and Workflows.

Currently, Centra's primary message broker provider is **RabbitMQ** (`Centra.Providers.RabbitMQ` and `Centra.Providers.RabbitMQ.Hosting`). While robust for enterprise AMQP broker topologies, RabbitMQ introduces broker-side disk queuing, Erlang VM operational overhead (~550 MB RAM), and publish latencies of $1.5\text{ms} - 15\text{ms}$.

This plan specifies the creation of a new, high-performance, consensus-backed Centra Pub/Sub provider:
**`Centra.Providers.Flotilla`** and **`Centra.Providers.Flotilla.Hosting`**.

Flotilla is a zero-allocation, sans-I/O Raft consensus engine in safe Rust, delivering:
- **Sub-80µs Quorum Commit Latency** (~100x faster than RabbitMQ).
- **Strict Monotonic Total Ordering** across cluster quorums.
- **Microsecond Memory Footprint** (< 25 MB RAM per node).
- **Pluggable Async Cosmos DB Offload Sink** for 90-day searchable history at zero broker cost.

Because Centra abstracts pub/sub via `Centra.PubSub.Abstractions` (`IPubSubDriver`, `IPubSubPublisher`, `IPubSubSubscriber`), applications using Centra event handlers (`IEventHandler<T>`) can switch from RabbitMQ to Flotilla with **zero code modifications to business consumers**.

```
CENTRA PUBSUB ARCHITECTURE WITH FLOTILLA PROVIDER:

[ Application Event Publisher ]
              │
              ▼ PublishAsync(topic, payload, metadata)
[ Centra.PubSub Router ]
              │
              ▼ IPubSubDriver
[ Centra.Providers.Flotilla.PubSubDriver ]
              │
              ▼ UDP Binary Framing (Zero-Alloc, CRC32) (< 80µs)
[ Flotilla Raft Consensus Cluster ]
   ├── Node 1 (Leader)
   ├── Node 2 (Follower)
   └── Node 3 (Follower)
              │
              ▼ Committed Log Index Stream (SubscribeCommitsAsync)
[ Centra.Providers.Flotilla.SubscriptionWorker ]
              │
              ▼ Dispatches to registered handlers
[ Application IEventHandler<TMessage> ] (100% UNCHANGED!)
```

---

## 1. Solution Structure & Project Layout

Two new projects will be added to the Centra solution (`Centra.slnx`):

```
src/
├── Centra.Providers.Flotilla/
│   ├── Centra.Providers.Flotilla.csproj
│   ├── Options/
│   │   └── FlotillaProviderOptions.cs
│   ├── Protocol/
│   │   ├── FlotillaFrameType.cs
│   │   ├── FlotillaWireProtocol.cs
│   │   └── FlotillaPacketHeader.cs
│   ├── Client/
│   │   ├── IFlotillaClient.cs
│   │   ├── FlotillaUdpClient.cs
│   │   ├── FlotillaProposalResult.cs
│   │   └── CommittedEntry.cs
│   └── PubSub/
│       ├── FlotillaPubSubDriver.cs
│       ├── FlotillaSubscription.cs
│       └── FlotillaSubscriptionWorker.cs
│
├── Centra.Providers.Flotilla.Hosting/
│   ├── Centra.Providers.Flotilla.Hosting.csproj
│   └── Extensions/
│       ├── CentraFlotillaServiceCollectionExtensions.cs
│       ├── FlotillaComponentInitializer.cs
│       └── FlotillaDelegateComponentInitializer.cs
│
tests/
└── Centra.Providers.Flotilla.Tests.Unit/
    ├── Centra.Providers.Flotilla.Tests.Unit.csproj
    ├── FlotillaWireProtocolTests.cs
    ├── FlotillaPubSubDriverTests.cs
    └── FlotillaHostingExtensionsTests.cs
```

---

## 2. Project Files Specification

### A. `src/Centra.Providers.Flotilla/Centra.Providers.Flotilla.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>High-throughput, microsecond-latency Raft consensus pub/sub provider for Centra backed by Flotilla.</Description>
    <PackageTags>$(PackageTags);providers;flotilla;raft;consensus;pubsub;messaging;cloudevents</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Centra.Runtime\Centra.Runtime.csproj" />
    <ProjectReference Include="..\Centra.PubSub.Abstractions\Centra.PubSub.Abstractions.csproj" />
    <ProjectReference Include="..\Centra.Events.Abstractions\Centra.Events.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```

### B. `src/Centra.Providers.Flotilla.Hosting/Centra.Providers.Flotilla.Hosting.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Hosting and dependency injection extensions for Centra Flotilla provider.</Description>
    <RootNamespace>Centra.Providers.Flotilla.Hosting</RootNamespace>
    <PackageTags>$(PackageTags);providers;flotilla;hosting;dependency-injection</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Centra.Providers.Flotilla\Centra.Providers.Flotilla.csproj" />
    <ProjectReference Include="..\Centra.Runtime\Centra.Runtime.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```

---

## 3. Core Component Implementation Details

### A. Configuration Options (`FlotillaProviderOptions.cs`)
```csharp
namespace Centra.Providers.Flotilla.Options;

public sealed class FlotillaProviderOptions
{
    public string[] ClusterNodes { get; set; } = ["127.0.0.1:9001", "127.0.0.1:9002", "127.0.0.1:9003"];
    public string DefaultPubSubName { get; set; } = "default";
    public int ClientTimeoutMs { get; set; } = 100;
    public int MaxBatchSize { get; set; } = 100;
    public bool EnableChecksumVerification { get; set; } = true;
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
```

### B. Wire Protocol Framing (`FlotillaWireProtocol.cs`)
Encodes and decodes topic, CloudEvents metadata headers, and domain payloads over binary spans:

```csharp
namespace Centra.Providers.Flotilla.Protocol;

public static class FlotillaWireProtocol
{
    private const uint Magic = 0x464C4F54; // 'FLOT'

    public static byte[] EncodeMessage(
        string topic,
        IReadOnlyDictionary<string, string>? metadata,
        ReadOnlySpan<byte> payload)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        writer.Write(Magic);
        writer.Write(topic);

        if (metadata != null && metadata.Count > 0)
        {
            writer.Write(metadata.Count);
            foreach (var (k, v) in metadata)
            {
                writer.Write(k);
                writer.Write(v ?? string.Empty);
            }
        }
        else
        {
            writer.Write(0);
        }

        writer.Write(payload.Length);
        writer.Write(payload);

        return ms.ToArray();
    }

    public static (string Topic, Dictionary<string, string> Metadata, ReadOnlyMemory<byte> Payload) DecodeMessage(
        ReadOnlyMemory<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        var magic = reader.ReadUInt32();
        if (magic != Magic)
        {
            throw new InvalidDataException("Invalid Flotilla message magic bytes");
        }

        var topic = reader.ReadString();
        var metaCount = reader.ReadInt32();
        var metadata = new Dictionary<string, string>(metaCount);

        for (int i = 0; i < metaCount; i++)
        {
            var k = reader.ReadString();
            var v = reader.ReadString();
            metadata[k] = v;
        }

        var payloadLen = reader.ReadInt32();
        var payloadBytes = reader.ReadBytes(payloadLen);

        return (topic, metadata, payloadBytes);
    }
}
```

---

### C. Driver Implementation (`FlotillaPubSubDriver.cs`)
Implements Centra's core `IPubSubDriver`, `IPubSubShutdownDrain`, and `IAsyncDisposable`:

```csharp
namespace Centra.Providers.Flotilla.PubSub;

public sealed class FlotillaPubSubDriver : IPubSubDriver, IPubSubShutdownDrain, IAsyncDisposable
{
    private readonly IFlotillaClient _client;
    private readonly FlotillaProviderOptions _options;
    private readonly ILogger<FlotillaPubSubDriver> _logger;
    private readonly ConcurrentDictionary<string, List<FlotillaSubscription>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _drainCts = new();
    private int _disposed;

    public FlotillaPubSubDriver(
        IFlotillaClient client,
        IOptions<FlotillaProviderOptions> options,
        ILogger<FlotillaPubSubDriver> logger)
    {
        _client = client;
        _options = options?.Value ?? new FlotillaProviderOptions();
        _logger = logger;
    }

    public async ValueTask PublishAsync(
        string pubSubName,
        string topic,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var encoded = FlotillaWireProtocol.EncodeMessage(topic, metadata, payload.Span);
        var result = await _client.ProposeAsync(encoded, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Flotilla proposal failed: {result.ErrorMessage}");
        }
    }

    public async ValueTask PublishBatchAsync(
        string pubSubName,
        string topic,
        IReadOnlyList<PubSubMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var msg in messages)
        {
            await PublishAsync(pubSubName, topic, msg.Payload, msg.Metadata ?? new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask SubscribeAsync(
        string pubSubName,
        string topic,
        Func<ReadOnlyMemory<byte>, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<EventHandlingResult>> handler,
        string? deadLetterTopic = null,
        CancellationToken cancellationToken = default,
        PubSubSubscribeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pubSubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        var sub = new FlotillaSubscription(topic, handler, deadLetterTopic, options);
        var list = _subscriptions.GetOrAdd(topic, _ => new List<FlotillaSubscription>());
        lock (list)
        {
            list.Add(sub);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(string pubSubName, string topic, CancellationToken cancellationToken = default)
    {
        _subscriptions.TryRemove(topic, out _);
        return ValueTask.CompletedTask;
    }

    public void BeginShutdownDrain()
    {
        _drainCts.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _drainCts.Cancel();
        _drainCts.Dispose();
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}
```

---

### D. Hosting Extension Method (`CentraFlotillaServiceCollectionExtensions.cs`)
```csharp
namespace Centra.Providers.Flotilla.Extensions;

public static class CentraFlotillaServiceCollectionExtensions
{
    public static IServiceCollection AddCentraFlotilla(
        this IServiceCollection services,
        Action<FlotillaProviderOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<FlotillaProviderOptions>();
        }

        services.TryAddSingleton<IFlotillaClient, FlotillaUdpClient>();
        services.TryAddSingleton<FlotillaPubSubDriver>();
        services.AddSingleton<IPubSubDriver>(sp => sp.GetRequiredService<FlotillaPubSubDriver>());
        services.AddSingleton<IPubSubShutdownDrain>(sp => sp.GetRequiredService<FlotillaPubSubDriver>());
        services.AddSingleton<IComponentInitializer, FlotillaComponentInitializer>();
        services.AddHostedService<FlotillaSubscriptionWorker>();

        return services;
    }
}
```

---

## 4. Documentation: `docs/providers/flotilla.md`

File: `docs/providers/flotilla.md`
Describes package usage, configuration parameters, and Raft consensus semantics following Centra's established documentation standards.

---

## 5. Verification Plan

### Automated Unit Tests
Project: `tests/Centra.Providers.Flotilla.Tests.Unit`
1. `FlotillaWireProtocolTests`:
   - Validates roundtrip encoding/decoding of topic, CloudEvent headers, and payload.
   - Asserts magic byte validation and error handling on corrupted buffers.
2. `FlotillaPubSubDriverTests`:
   - Validates `PublishAsync` submits proposals to `IFlotillaClient`.
   - Validates `PublishBatchAsync` handles multiple messages.
   - Validates `SubscribeAsync` registers handlers and `UnsubscribeAsync` clears them.
   - Validates `BeginShutdownDrain` triggers cancellation tokens.
3. `FlotillaHostingExtensionsTests`:
   - Verifies DI registration of `FlotillaPubSubDriver`, `IPubSubDriver`, and options binding.

### Command:
```bash
dotnet test /home/chad/source/dotnet/distributed-framework/tests/Centra.Providers.Flotilla.Tests.Unit/Centra.Providers.Flotilla.Tests.Unit.csproj
```
