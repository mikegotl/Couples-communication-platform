using System.Security.Cryptography;
using System.Text;
using Couples.Application.Feedback;
using Couples.Application.Observability;
using Couples.Application.Pilot;
using Couples.Application.Privacy;
using Couples.Domain.Common;
using Couples.Domain.Feedback;
using Couples.Domain.Pilot;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Pilot;

/// <summary>Milestone 11: consent-based enrollment and pilot feedback.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class PilotAndFeedbackTests(PostgresFixture db)
{
    private const string Terms = PilotDocuments.TermsVersion;
    private const string Notice = PilotDocuments.PrivacyNoticeVersion;

    [Fact]
    public async Task EnrollmentNeedsTheCurrentDocumentsAndTheInvitationCode()
    {
        var a = await db.CreateUserAsync();
        var code = Hash("spring-pilot");

        Assert.Equal(DomainError.ConsentMismatch, (await Pilot(s => s.EnrollAsync(a, "pilot-terms-old", Notice, "spring-pilot"), code)).Error);
        Assert.Equal(DomainError.Validation, (await Pilot(s => s.EnrollAsync(a, Terms, Notice, "wrong"), code)).Error);
        Assert.False(await Pilot(s => s.IsEnrolledAsync(a), code));

        var enrolled = await Pilot(s => s.EnrollAsync(a, Terms, Notice, " spring-pilot "), code);
        Assert.True(enrolled.Value.Enrolled);
        Assert.True(await Pilot(s => s.IsEnrolledAsync(a), code));
    }

    [Fact]
    public async Task WithdrawingEndsAccessAndRejoiningNeedsTheCodeAgain()
    {
        var a = await db.CreateUserAsync();
        var code = Hash("c");
        await Pilot(s => s.EnrollAsync(a, Terms, Notice, "c"), code);

        var withdrawn = await Pilot(s => s.WithdrawAsync(a), code);
        Assert.True(withdrawn.Withdrawn);
        Assert.False(await Pilot(s => s.IsEnrolledAsync(a), code));
        Assert.Equal(DomainError.Validation, (await Pilot(s => s.EnrollAsync(a, Terms, Notice, null), code)).Error);
        Assert.True((await Pilot(s => s.EnrollAsync(a, Terms, Notice, "c"), code)).Value.Enrolled);

        await using var context = Context();
        Assert.Equal(2, await context.AuditEvents.CountAsync(e => e.ActorUserId == a && e.Action == "pilot.enrolled"));
        Assert.Equal(1, await context.AuditEvents.CountAsync(e => e.ActorUserId == a && e.Action == "pilot.withdrawn"));
    }

    [Fact]
    public async Task UpdatedDocumentsAskParticipantsToAgreeAgainWithoutACode()
    {
        var a = await db.CreateUserAsync();
        var code = Hash("c");
        await Pilot(s => s.EnrollAsync(a, Terms, Notice, "c"), code);

        // Simulate the terms having changed since they agreed.
        await using (var context = Context())
        {
            await context.PilotEnrollments.Where(e => e.UserId == a)
                .ExecuteUpdateAsync(set => set.SetProperty(e => e.TermsVersion, "pilot-terms-2026-10"));
        }

        var status = await Pilot(s => s.GetStatusAsync(a), code);
        Assert.True(status.NeedsReconsent);
        Assert.False(await Pilot(s => s.IsEnrolledAsync(a), code));
        Assert.True((await Pilot(s => s.EnrollAsync(a, Terms, Notice, null), code)).Value.Enrolled);
    }

    [Fact]
    public async Task ThePilotStopsAtItsCapacity()
    {
        var a = await db.CreateUserAsync();
        await using var context = Context();
        var active = await context.PilotEnrollments.CountAsync(e => e.WithdrawnAt == null);

        var result = await Pilot(s => s.EnrollAsync(a, Terms, Notice, null), code: null, maxParticipants: active);

        Assert.Equal(DomainError.PilotFull, result.Error);
    }

    [Fact]
    public async Task FeedbackIsAnonymousUnlessContactIsAllowedAndLimitedPerDay()
    {
        var a = await db.CreateUserAsync();
        var marker = "Feedback " + Guid.NewGuid().ToString("N");
        Assert.True((await Feedback(s => s.SubmitAsync(a, FeedbackArea.Messaging, 4, marker + " anon", allowContact: false))).Succeeded);
        Assert.True((await Feedback(s => s.SubmitAsync(a, FeedbackArea.Coach, null, marker + " linked", allowContact: true))).Succeeded);
        Assert.Equal(DomainError.Validation, (await Feedback(s => s.SubmitAsync(a, FeedbackArea.General, null, "  ", false))).Error);
        Assert.Equal(DomainError.Validation, (await Feedback(s => s.SubmitAsync(a, FeedbackArea.General, 6, null, false))).Error);

        await using (var context = Context())
        {
            var rows = await context.Feedback.Where(f => f.Text!.StartsWith(marker)).ToListAsync();
            Assert.Null(rows.Single(r => r.Text!.EndsWith("anon", StringComparison.Ordinal)).ContactUserId);
            Assert.Equal(a, rows.Single(r => r.Text!.EndsWith("linked", StringComparison.Ordinal)).ContactUserId);
        }

        // Export holds only the linked entry; deletion removes it and keeps the anonymous one.
        var export = await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(a));
        Assert.Equal([marker + " linked"], export.Feedback!.Select(f => f.Text));
        await Run<IPersonalDataService, Guid>(s => s.RequestDeletionAsync(a));
        await Run<IPersonalDataService, int>(s => s.ProcessPendingDeletionsAsync());
        await using (var context = Context())
        {
            Assert.Equal([marker + " anon"], await context.Feedback.Where(f => f.Text!.StartsWith(marker)).Select(f => f.Text).ToListAsync());
        }
    }

    [Fact]
    public async Task FeedbackIsLimitedToFivePerPersonPerDay()
    {
        var a = await db.CreateUserAsync();
        await using var provider = db.BuildServices();

        // One provider so the in-memory counter is shared, as it is in the running app.
        async Task<Result> Submit()
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IFeedbackService>().SubmitAsync(a, FeedbackArea.General, 3, null, false);
        }

        for (var i = 0; i < IFeedbackService.DailyLimit; i++)
        {
            Assert.True((await Submit()).Succeeded);
        }

        Assert.Equal(DomainError.RateLimited, (await Submit()).Error);
    }

    [Fact]
    public async Task OperatorCountsHideSmallNumbers()
    {
        var snapshot = await Run<IOperationsOverview, OperationsSnapshot>(s => s.GetAsync());
        await using var context = Context();
        var accounts = await context.Users.LongCountAsync();
        var pending = await context.DataDeletionRequests.LongCountAsync(r => r.Status == Couples.Domain.Privacy.DeletionStatus.Pending);

        Assert.Equal(accounts >= 5 ? accounts : null, snapshot.Accounts);
        Assert.Equal(pending >= 5 ? pending : null, snapshot.PendingDeletions);
        Assert.All([snapshot.Accounts, snapshot.PilotParticipants, snapshot.ActiveWorkspaces, snapshot.MessagesSharedLast7Days, snapshot.FeedbackLast30Days],
            v => Assert.True(v is null or >= OperationsSnapshot.SuppressBelow));
    }

    [Fact]
    public async Task OldFeedbackIsPurged()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var a = await db.CreateUserAsync();
        var marker = "Old " + Guid.NewGuid().ToString("N");
        await using (var provider = db.BuildServices(clock))
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IFeedbackService>().SubmitAsync(a, FeedbackArea.General, 3, marker, false);
        }

        clock.Advance(TimeSpan.FromDays(366));
        await using (var provider = db.BuildServices(clock))
        await using (var scope = provider.CreateAsyncScope())
        {
            var report = await scope.ServiceProvider.GetRequiredService<IRetentionService>().ApplyAsync();
            Assert.True(report.Feedback >= 1);
        }

        await using var context = Context();
        Assert.False(await context.Feedback.AnyAsync(f => f.Text == marker));
    }

    private static string Hash(string code) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private async Task<T> Pilot<T>(Func<IPilotService, Task<T>> call, string? code, int maxParticipants = 100_000)
    {
        await using var provider = db.BuildServices(configure: s => s.Configure<PilotOptions>(o =>
        {
            o.AccessCodeSha256 = code;
            o.MaxParticipants = maxParticipants;
        }));
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<IPilotService>());
    }

    private Task<T> Feedback<T>(Func<IFeedbackService, Task<T>> call) => Run(call);

    private CouplesDbContext Context() => db.BuildServices().CreateScope().ServiceProvider.GetRequiredService<CouplesDbContext>();

    private async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> call)
        where TService : notnull
    {
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
