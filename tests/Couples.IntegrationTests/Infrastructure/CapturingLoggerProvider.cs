using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Couples.IntegrationTests.Infrastructure;

/// <summary>Records every log line (message, state values and exception text) for assertions.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> lines = new();

    public IReadOnlyCollection<string> Lines => lines;

    public string AllText => string.Join('\n', lines);

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(" ", pairs.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} | {values} | {exception}");
        }
    }
}
