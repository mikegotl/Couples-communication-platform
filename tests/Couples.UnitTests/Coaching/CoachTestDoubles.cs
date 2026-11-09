using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Couples.AI;
using Couples.Application.Coaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Couples.UnitTests.Coaching;

internal sealed class ScriptedModel(params Func<ModelPrompt, CancellationToken, Task<ModelResponse>>[] script) : ICoachModelClient
{
    private int calls;

    public string Name => "scripted";

    public List<ModelPrompt> Prompts { get; } = [];

    public int Calls => calls;

    public Task<ModelResponse> CompleteAsync(ModelPrompt prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        var index = Interlocked.Increment(ref calls) - 1;
        return script[Math.Min(index, script.Length - 1)](prompt, cancellationToken);
    }

    public static Func<ModelPrompt, CancellationToken, Task<ModelResponse>> Returns(string content, bool refused = false) =>
        (_, _) => Task.FromResult(new ModelResponse(content, 100, 50, refused));

    public static Func<ModelPrompt, CancellationToken, Task<ModelResponse>> Throws(Exception ex) => (_, _) => Task.FromException<ModelResponse>(ex);

    public static Func<ModelPrompt, CancellationToken, Task<ModelResponse>> Hangs() =>
        async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        };

    public static Func<ModelPrompt, CancellationToken, Task<ModelResponse>> Echo(Func<ModelPrompt, string> content) =>
        (p, _) => Task.FromResult(new ModelResponse(content(p), 100, 50));
}

internal sealed class MemoryUsageStore : ICoachUsageStore
{
    public Exception? FailReserve { get; set; }

    public Exception? FailRecord { get; set; }

    private readonly ConcurrentDictionary<(Guid, DateOnly), int> requests = new();

    public int Reservations { get; private set; }

    public long Tokens { get; private set; }

    public Task<bool> TryReserveAsync(Guid userId, DateOnly day, int perUserLimit, int globalRequestLimit, long globalTokenLimit, CancellationToken cancellationToken = default)
    {
        if (FailReserve is not null)
        {
            return Task.FromException<bool>(FailReserve);
        }

        var count = requests.GetOrAdd((userId, day), 0);
        if (count >= perUserLimit || Tokens >= globalTokenLimit)
        {
            return Task.FromResult(false);
        }

        requests[(userId, day)] = count + 1;
        Reservations++;
        return Task.FromResult(true);
    }

    public Task RecordTokensAsync(Guid userId, DateOnly day, int inputTokens, int outputTokens, CancellationToken cancellationToken = default)
    {
        if (FailRecord is not null)
        {
            return Task.FromException(FailRecord);
        }

        Tokens += inputTokens + outputTokens;
        return Task.CompletedTask;
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Lines)
        {
            Lines.Add(formatter(state, exception) + " " + exception);
        }
    }
}

internal static class Coach
{
    private static readonly IMeterFactory Meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    public static CoachingOptions FastOptions() => new()
    {
        Provider = CoachProvider.Fake,
        RetryBaseDelay = TimeSpan.Zero,
        AttemptTimeout = TimeSpan.FromMilliseconds(200),
        OverallTimeout = TimeSpan.FromSeconds(5),
    };

    public static CoachService Create(ICoachModelClient? model, MemoryUsageStore? usage = null, CoachingOptions? options = null, CapturingLogger<CoachService>? logger = null) =>
        new(model, usage ?? new MemoryUsageStore(), Options.Create(options ?? FastOptions()), TimeProvider.System, new CoachTelemetry(Meters), logger ?? new CapturingLogger<CoachService>());

    public static string Json(string text = "I'd like us to plan a weekend together.", string explanation = "It names a wish instead of a complaint.") =>
        $$"""{"declined": false, "safety_concern": false, "suggestions": [{"text": "{{text}}", "explanation": "{{explanation}}"}]}""";
}
