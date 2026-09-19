using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

/// <summary>
/// Captures formatted log entries so tests can assert that a condition was reported rather than swallowed.
/// </summary>
internal sealed class RecordingLogger<TCategory> : ILogger<TCategory>
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Enqueue((logLevel, formatter(state, exception)));
}
