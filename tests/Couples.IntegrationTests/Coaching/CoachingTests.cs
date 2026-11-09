using System.Text.Json;
using System.Text.RegularExpressions;
using Couples.AI;
using Couples.Application.Coaching;
using Couples.Application.Privacy;
using Couples.Domain.Coaching;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Coaching;

/// <summary>
/// Milestone 06 gate: the coach works end to end with the fake provider, daily caps hold under
/// concurrency, usage is exported, erased and purged, and nothing leaks into logs.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed partial class CoachingTests(PostgresFixture db)
{
    private static int dayOffset;

    [Fact]
    public async Task FakeProviderSuggestsAndRecordsUsageWithoutLoggingText()
    {
        var a = await db.CreateUserAsync();
        var logs = new CapturingLoggerProvider();
        // Letters only: a random hex id can contain a digit run that reads like a phone number.
        var draft = $"You never call me back {new string(Guid.NewGuid().ToString("N").Select(c => (char)('a' + (c % 26))).ToArray())}";

        var result = await RunCoach(c => c.SuggestAsync(new CoachRequest(a, CoachTask.RephraseDraft, draft)), logs);

        Assert.Equal(CoachOutcome.Suggested, result.Outcome);
        Assert.InRange(result.Suggestions.Count, 1, 3);
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<CouplesDbContext>().CoachingUsage.SingleAsync(u => u.UserId == a);
        Assert.Equal(1, row.Requests);
        Assert.True(row.InputTokens > 0 && row.OutputTokens > 0);
        Assert.Contains(logs.Lines, l => l.Contains("outcome Suggested", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, l => l.Contains(draft[^32..], StringComparison.Ordinal) || l.Contains("call me back", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PerUserDailyCapHoldsUnderConcurrency()
    {
        var a = await db.CreateUserAsync();
        var day = UniqueDay();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Reserve(a, day, perUser: 5)));

        Assert.Equal(5, results.Count(r => r));
        Assert.Equal(5, await Requests(a, day));
        Assert.True(await Reserve(a, day.AddDays(1), perUser: 5));
    }

    [Fact]
    public async Task ServiceWideRequestAndTokenCapsStopNewRequests()
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var day = UniqueDay();

        Assert.True(await Reserve(a, day, perUser: 10, globalRequests: 2));
        Assert.True(await Reserve(b, day, perUser: 10, globalRequests: 2));
        Assert.False(await Reserve(b, day, perUser: 10, globalRequests: 2));

        var tokenDay = UniqueDay();
        Assert.True(await Reserve(a, tokenDay, perUser: 10, globalTokens: 100));
        await Store(s => s.RecordTokensAsync(a, tokenDay, 80, 30));
        Assert.False(await Reserve(b, tokenDay, perUser: 10, globalTokens: 100));
    }

    [Fact]
    public async Task UsageIsExportedErasedAndPurged()
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var today = DateOnly.FromDateTime(db.Clock.UtcNow.UtcDateTime);
        var old = today.AddDays(-91);
        Assert.True(await Reserve(a, today, perUser: 5));
        await Store(s => s.RecordTokensAsync(a, today, 10, 5));
        Assert.True(await Reserve(b, old, perUser: 5));
        Assert.True(await Reserve(b, today, perUser: 5));

        var export = await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(a));
        var exported = Assert.Single(export.CoachingUsage);
        Assert.Equal((today, 1, 10, 5), (exported.Day, exported.Requests, exported.InputTokens, exported.OutputTokens));
        Assert.DoesNotContain(b.ToString(), JsonSerializer.Serialize(export), StringComparison.Ordinal);

        var report = await Run<IRetentionService, RetentionReport>(s => s.ApplyAsync());
        Assert.True(report.CoachingUsageDays >= 1);
        Assert.Equal(0, await Requests(b, old));
        Assert.Equal(1, await Requests(b, today));

        await Run<IPersonalDataService, Guid>(s => s.RequestDeletionAsync(a));
        await Run<IPersonalDataService, int>(s => s.ProcessPendingDeletionsAsync());
        Assert.Equal(0, await Requests(a, today));
    }

    [Fact]
    public void RepositoryContainsNoProviderSecrets()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Couples.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var skip = new[] { $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}" };
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".cs", ".razor", ".md", ".yml", ".yaml", ".env", ".config", ".xml", ".props" };
        var offenders = Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)) && !skip.Any(s => f.Contains(s, StringComparison.Ordinal)))
            .Where(f => SecretPattern().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root.FullName, f))
            .ToList();

        Assert.Empty(offenders);

        foreach (var settings in Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "Couples.Web"), "appsettings*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settings), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.TryGetProperty("Coaching", out var coaching))
            {
                if (coaching.TryGetProperty("Gateway", out var gateway) && gateway.TryGetProperty("ApiKey", out var key))
                {
                    Assert.True(key.ValueKind == JsonValueKind.Null || key.GetString() is "", $"{settings} must not contain a coaching API key.");
                }

                if (coaching.TryGetProperty("ApprovedForUserContent", out var approved))
                {
                    Assert.Equal(JsonValueKind.False, approved.ValueKind);
                }
            }
        }
    }

    // Well-known live key shapes (OpenAI/Anthropic-style, AWS, GitHub, Slack, private keys).
    [GeneratedRegex(@"\b(sk-(ant-)?[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{36}|xox[baprs]-[A-Za-z0-9-]{10,})\b|-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex SecretPattern();

    private static DateOnly UniqueDay() => new DateOnly(2040, 1, 1).AddDays(Interlocked.Increment(ref dayOffset) + Random.Shared.Next(0, 100_000));

    private async Task<bool> Reserve(Guid user, DateOnly day, int perUser, int globalRequests = 1_000_000, long globalTokens = 1_000_000_000) =>
        await Store(s => s.TryReserveAsync(user, day, perUser, globalRequests, globalTokens));

    private async Task<int> Requests(Guid user, DateOnly day)
    {
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CouplesDbContext>().CoachingUsage
            .Where(u => u.UserId == user && u.Day == day).Select(u => (int?)u.Requests).SingleOrDefaultAsync() ?? 0;
    }

    private async Task<T> Store<T>(Func<ICoachUsageStore, Task<T>> call) => await Run(call);

    private async Task Store(Func<ICoachUsageStore, Task> call) => await Run<ICoachUsageStore, bool>(async s =>
    {
        await call(s);
        return true;
    });

    private async Task<CoachResult> RunCoach(Func<ICommunicationCoach, Task<CoachResult>> call, CapturingLoggerProvider? logs = null)
    {
        await using var provider = db.BuildServices(logs: logs, configure: services =>
        {
            services.AddCoaching();
            services.Configure<CoachingOptions>(o => o.Provider = CoachProvider.Fake);
        });
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<ICommunicationCoach>());
    }

    private async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> call)
        where TService : notnull
    {
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
