using Couples.Application.JointChat;
using Couples.Application.Privacy;
using Couples.Application.Safety;
using Couples.Application.Workspaces;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.Consent;
using Couples.Domain.JointChat;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.JointChat;

/// <summary>
/// Milestone 12 gate: with the flag off nothing about joint chat works or is written; with it on
/// (and sign-offs and eligibility), the opt-in pilot rules hold on the server.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class JointChatPilotTests(PostgresFixture db)
{
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task WithTheFlagOffOrWithoutSignOffNothingWorksAndNothingIsWritten(bool flag, bool signedOff)
    {
        var (a, b, workspace) = await PairAsync(eligible: true);
        var off = JointChatPilot.On(flag, signedOff);

        Assert.False(await Chat(s => s.IsOfferedAsync(a), off));
        Assert.Equal(JointChatState.Unavailable, (await Chat(s => s.GetAsync(a), off)).State);
        Assert.Equal(DomainError.NotFound, (await Chat(s => s.StartAsync(a, JointChatPolicy.Version), off)).Error);
        var anyId = Guid.CreateVersion7();
        Assert.Equal(DomainError.NotFound, (await Chat(s => s.JoinAsync(b, anyId, JointChatPolicy.Version), off)).Error);
        Assert.Equal(DomainError.NotFound, (await Chat(s => s.SendAsync(a, anyId, "Hello"), off)).Error);
        Assert.Equal(DomainError.NotFound, (await Chat(s => s.PauseAsync(a, anyId, 5), off)).Error);
        Assert.Equal(DomainError.NotFound, (await Chat(s => s.DeclineAsync(b, anyId), off)).Error);

        await using var context = Context();
        Assert.False(await context.JointChatSessions.AnyAsync(s => s.WorkspaceId == workspace));
        Assert.False(await context.ConsentRecords.AnyAsync(c => c.UserId == a && c.Purpose == ConsentPurpose.JointChatSession));
    }

    [Fact]
    public async Task TheDefaultConfigurationKeepsThePilotOff()
    {
        var (a, _, _) = await PairAsync(eligible: true);

        // No configure: the flag is read from (empty) configuration, as in production by default.
        await using var provider = db.BuildServices(clock);
        await using var scope = provider.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IJointChatService>();
        Assert.False(await chat.IsOfferedAsync(a));
        Assert.Equal(DomainError.NotFound, (await chat.StartAsync(a, JointChatPolicy.Version)).Error);
    }

    [Fact]
    public async Task OnlyEligibleWorkspacesWithTwoPartnersAreOffered()
    {
        var (a, _, _) = await PairAsync(eligible: false);
        Assert.Equal(JointChatState.Unavailable, (await Chat(s => s.GetAsync(a))).State);
        Assert.Equal(DomainError.NotFound, (await Chat(s => s.StartAsync(a, JointChatPolicy.Version))).Error);

        var solo = await db.CreateUserAsync();
        var own = await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.CreateAsync(solo, "Just me"));
        await using (var context = Context())
        {
            await JointChatPilot.MakeEligibleAsync(context, own.Value.Id, solo, clock.UtcNow);
        }

        Assert.Equal(JointChatState.Unavailable, (await Chat(s => s.GetAsync(solo))).State);
    }

    [Fact]
    public async Task BothPartnersConsentFreshlyToEachChat()
    {
        var (a, b, _) = await PairAsync(eligible: true);
        Assert.Equal(DomainError.ConsentMismatch, (await Chat(s => s.StartAsync(a, "an-older-policy"))).Error);

        var started = await Chat(s => s.StartAsync(a, JointChatPolicy.Version));
        Assert.Equal(JointChatState.WaitingForPartner, started.Value.State);
        var invite = await Chat(s => s.GetAsync(b));
        Assert.Equal(JointChatState.InvitedByPartner, invite.State);
        Assert.Empty(invite.Transcript);

        var joined = await Chat(s => s.JoinAsync(b, invite.SessionId!.Value, JointChatPolicy.Version));
        Assert.Equal(JointChatState.Active, joined.Value.State);

        await Chat(s => s.LeaveAsync(a, invite.SessionId!.Value));
        var second = await Chat(s => s.StartAsync(b, JointChatPolicy.Version));
        Assert.Equal(JointChatState.WaitingForPartner, second.Value.State);

        // Each chat has its own consent from each partner; agreeing to the first doesn't carry over.
        await using var context = Context();
        var consents = await context.ConsentRecords.Where(c => c.Purpose == ConsentPurpose.JointChatSession && (c.UserId == a || c.UserId == b)).ToListAsync();
        Assert.Equal(2, consents.Count(c => c.SubjectId == invite.SessionId));
        Assert.Single(consents, c => c.SubjectId == second.Value.SessionId);
        Assert.All(consents, c => Assert.Equal(ContentFingerprint.Of(JointChatPolicy.Version), c.ContentFingerprint));
        Assert.Equal(JointChatState.InvitedByPartner, (await Chat(s => s.GetAsync(a))).State);
    }

    [Fact]
    public async Task DecliningIsPrivateAndOneChatRunsAtATime()
    {
        var (a, b, _) = await PairAsync(eligible: true);
        var started = await Chat(s => s.StartAsync(a, JointChatPolicy.Version));
        Assert.Equal(DomainError.Conflict, (await Chat(s => s.StartAsync(b, JointChatPolicy.Version))).Error);

        var declined = await Chat(s => s.DeclineAsync(b, started.Value.SessionId!.Value));
        Assert.Equal(JointChatEnding.YouDeclined, declined.Value.Ending);
        Assert.Equal(JointChatEnding.Ended, (await Chat(s => s.GetAsync(a))).Ending);
    }

    [Fact]
    public async Task TurnTakingPauseAndLeavingAreEnforcedOnTheServer()
    {
        var (a, b, _) = await PairAsync(eligible: true);
        var id = await ActiveChatAsync(a, b);

        Assert.Equal(DomainError.Conflict, (await Chat(s => s.SendAsync(b, id, "Me first?"))).Error);
        var sent = await Chat(s => s.SendAsync(a, id, "Thanks for joining. Can we talk about Sunday?"));
        Assert.Equal(ModerationOutcome.Allowed, sent.Value.Outcome);
        Assert.False(sent.Value.View.MyTurn);
        Assert.Equal(DomainError.Conflict, (await Chat(s => s.SendAsync(a, id, "And another thing"))).Error);

        var seenByB = await Chat(s => s.GetAsync(b));
        Assert.True(seenByB.MyTurn);
        Assert.Single(seenByB.Transcript, l => !l.Mine && l.Body.StartsWith("Thanks", StringComparison.Ordinal));

        Assert.True((await Chat(s => s.PauseAsync(b, id, 10))).Succeeded);
        Assert.Equal(DomainError.Conflict, (await Chat(s => s.SendAsync(b, id, "During the break"))).Error);
        Assert.Equal(DomainError.Conflict, (await Chat(s => s.ResumeAsync(a, id))).Error);
        Assert.True((await Chat(s => s.GetAsync(a))).Paused);
        Assert.True((await Chat(s => s.ResumeAsync(b, id))).Succeeded);
        Assert.True((await Chat(s => s.SendAsync(b, id, "Yes, Sunday works."))).Succeeded);

        // Either can leave at once, without the other; it ends for both.
        var left = await Chat(s => s.LeaveAsync(b, id));
        Assert.Equal(JointChatEnding.YouLeft, left.Value.Ending);
        var forA = await Chat(s => s.GetAsync(a));
        Assert.Equal(JointChatState.Ended, forA.State);
        Assert.Equal(JointChatEnding.Ended, forA.Ending);
        Assert.Equal(2, forA.Transcript.Count);
        Assert.Equal(DomainError.NotFound, (await Chat(s => s.SendAsync(a, id, "Wait"))).Error);
    }

    [Fact]
    public async Task TimeRulesApplyOnTheServer()
    {
        var (a, b, _) = await PairAsync(eligible: true);
        var id = await ActiveChatAsync(a, b);
        await Chat(s => s.SendAsync(a, id, "Hello"));

        clock.Advance(JointChatPolicy.TurnTimeout);
        var open = await Chat(s => s.GetAsync(a));
        Assert.True(open.OpenTurn && open.MyTurn);

        clock.Advance(JointChatPolicy.IdleTimeout);
        Assert.Equal(JointChatState.Ended, (await Chat(s => s.GetAsync(b))).State);

        var lapsing = await Chat(s => s.StartAsync(b, JointChatPolicy.Version));
        clock.Advance(JointChatPolicy.InvitationExpiry);
        Assert.Equal(JointChatEnding.NotJoined, (await Chat(s => s.GetAsync(b))).Ending);
        Assert.Equal(JointChatEnding.Ended, (await Chat(s => s.GetAsync(a))).Ending);
        Assert.NotNull(lapsing.Value.SessionId);
    }

    [Fact]
    public async Task ModerationHoldsContemptWithoutStoringItAndEndsForRiskShowingSupportOnlyToTheWriter()
    {
        var (a, b, _) = await PairAsync(eligible: true);
        var id = await ActiveChatAsync(a, b);

        var held = await Chat(s => s.SendAsync(a, id, "That was a stupid plan"));
        Assert.Equal(ModerationOutcome.HeldToRephrase, held.Value.Outcome);
        Assert.True(held.Value.View.MyTurn);
        await using (var context = Context())
        {
            Assert.False(await context.JointChatMessages.AnyAsync(m => m.SessionId == id));
        }

        Assert.True((await Chat(s => s.SendAsync(a, id, "I felt let down by the plan."))).Succeeded);
        var risky = await Chat(s => s.SendAsync(b, id, "I'm afraid to go home tonight"));
        Assert.Equal(ModerationOutcome.EndedForSafety, risky.Value.Outcome);

        // A sees only a neutral ending, the same as for any other, and never the text.
        var forA = await Chat(s => s.GetAsync(a));
        Assert.Equal(JointChatEnding.Ended, forA.Ending);
        Assert.DoesNotContain(forA.Transcript, l => l.Body.Contains("afraid", StringComparison.Ordinal));
        await using var check = Context();
        Assert.False(await check.JointChatMessages.AnyAsync(m => m.SessionId == id && m.Body.Contains("afraid")));
        Assert.Equal(JointChatEndReason.Unavailable, (await check.JointChatSessions.SingleAsync(s => s.Id == id)).EndReason);
    }

    [Fact]
    public async Task ASafetyHoldEndsTheChatSilentlyAndHidesInvitations()
    {
        var (a, b, _) = await PairAsync(eligible: true);
        var id = await ActiveChatAsync(a, b);
        await Chat(s => s.SendAsync(a, id, "Hello"));

        await Run<ISafetyService, SafetyStatus>(s => s.SetSafetyModeAsync(b, true));

        // A's next look ends the chat; A is told only that it ended.
        var forA = await Chat(s => s.GetAsync(a));
        Assert.Equal(JointChatEnding.Ended, forA.Ending);
        Assert.Equal(JointChatState.HeldForYou, (await Chat(s => s.GetAsync(b))).State);
        Assert.Equal(DomainError.SafetyHold, (await Chat(s => s.StartAsync(b, JointChatPolicy.Version))).Error);

        // A can still invite; B never sees it, and it lapses like any unanswered invitation.
        var invite = await Chat(s => s.StartAsync(a, JointChatPolicy.Version));
        Assert.Equal(JointChatState.WaitingForPartner, invite.Value.State);
        Assert.Equal(JointChatState.HeldForYou, (await Chat(s => s.GetAsync(b))).State);
        Assert.Equal(DomainError.SafetyHold, (await Chat(s => s.JoinAsync(b, invite.Value.SessionId!.Value, JointChatPolicy.Version))).Error);
        Assert.Equal(JointChatState.WaitingForPartner, (await Chat(s => s.GetAsync(a))).State);
        clock.Advance(JointChatPolicy.InvitationExpiry);
        Assert.Equal(JointChatEnding.NotJoined, (await Chat(s => s.GetAsync(a))).Ending);

        // No audit event or log line says anything about safety.
        await using var context = Context();
        var actions = await context.AuditEvents.Where(e => e.ActorUserId == a || e.ActorUserId == b).Select(e => e.Action).ToListAsync();
        Assert.DoesNotContain(actions, x => x.Contains("safety", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OutsidersCannotSeeOrTouchAChat()
    {
        var (a, b, _) = await PairAsync(eligible: true);
        var (c, d, _) = await PairAsync(eligible: true);
        var id = await ActiveChatAsync(a, b);

        Assert.Equal(DomainError.NotFound, (await Chat(s => s.SendAsync(c, id, "Hi"))).Error);
        Assert.Equal(DomainError.NotFound, (await Chat(s => s.LeaveAsync(c, id))).Error);
        Assert.Equal(DomainError.NotFound, (await Chat(s => s.PauseAsync(d, id, 5))).Error);
        Assert.Equal(JointChatState.Ready, (await Chat(s => s.GetAsync(c))).State);
        Assert.Equal(JointChatState.Active, (await Chat(s => s.GetAsync(a))).State);
    }

    [Fact]
    public async Task LeavingTheWorkspaceOrLosingEligibilityEndsTheChat()
    {
        var (a, b, workspace) = await PairAsync(eligible: true);
        var id = await ActiveChatAsync(a, b);
        Assert.True((await Run<IWorkspaceService, Result>(s => s.LeaveAsync(b, workspace))).Succeeded);
        Assert.Equal(JointChatState.Unavailable, (await Chat(s => s.GetAsync(a))).State);
        await using (var context = Context())
        {
            Assert.Equal(JointChatStatus.Ended, (await context.JointChatSessions.SingleAsync(s => s.Id == id)).Status);
        }

        var (c, d, other) = await PairAsync(eligible: true);
        var second = await ActiveChatAsync(c, d);
        var admin = await db.CreateUserAsync();
        await using (var context = Context())
        {
            await JointChatPilot.MakeAdminAsync(context, admin);
        }

        Assert.True((await Admin(s => s.RevokeAsync(admin, other))).Succeeded);
        await using var check = Context();
        Assert.Equal(JointChatEndReason.Unavailable, (await check.JointChatSessions.SingleAsync(s => s.Id == second)).EndReason);
        Assert.Equal(JointChatState.Unavailable, (await Chat(s => s.GetAsync(d))).State);
    }

    [Fact]
    public async Task OnlyAdminsManageEligibilityAndOnlyAfterBothSignOffs()
    {
        var (a, _, workspace) = await PairAsync(eligible: false);
        var admin = await db.CreateUserAsync();
        await using (var context = Context())
        {
            await JointChatPilot.MakeAdminAsync(context, admin);
        }

        Assert.Equal(DomainError.NotFound, (await Admin(s => s.GrantAsync(a, workspace, "P-1"))).Error);
        Assert.Equal(DomainError.NotFound, (await Admin(s => s.GetOverviewAsync(a))).Error);
        Assert.Equal(DomainError.NotFound, (await Admin(s => s.GrantAsync(admin, workspace, "P-1"), JointChatPilot.On(flag: false))).Error);
        Assert.Equal(DomainError.Conflict, (await Admin(s => s.GrantAsync(admin, workspace, "P-1"), JointChatPilot.On(signedOff: false))).Error);
        Assert.Equal(DomainError.Validation, (await Admin(s => s.GrantAsync(admin, workspace, ""))).Error);

        Assert.True((await Admin(s => s.GrantAsync(admin, workspace, "P-1"))).Succeeded);
        Assert.True(await Chat(s => s.IsOfferedAsync(a)));
        var overview = await Admin(s => s.GetOverviewAsync(admin));
        Assert.Contains(overview.Value.Workspaces, w => w.WorkspaceId == workspace && w.Reference == "P-1");

        await using var check = Context();
        Assert.True(await check.AuditEvents.AnyAsync(e => e.ActorUserId == admin && e.Action == AuditActions.JointChatEligibilityGranted && e.WorkspaceId == workspace));
    }

    [Fact]
    public async Task ExportIncludesTheChatAndRetentionDeletesItAWeekAfterItEnds()
    {
        var (a, b, _) = await PairAsync(eligible: true);
        var id = await ActiveChatAsync(a, b);
        await Chat(s => s.SendAsync(a, id, "Shall we plan the weekend?"));
        await Chat(s => s.SendAsync(b, id, "Yes please."));
        await Chat(s => s.LeaveAsync(a, id));

        var export = await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(b));
        var chat = Assert.Single(export.JointChats!, c => c.Id == id);
        Assert.Equal(["Shall we plan the weekend?", "Yes please."], chat.Lines.Select(l => l.Body));
        Assert.Equal([false, true], chat.Lines.Select(l => l.Mine));

        clock.Advance(JointChatPolicy.TranscriptKeptAfterEnd - TimeSpan.FromMinutes(1));
        Assert.Equal(2, (await Chat(s => s.GetAsync(a))).Transcript.Count);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(JointChatState.Ready, (await Chat(s => s.GetAsync(a))).State);
        await Run<IRetentionService, RetentionReport>(s => s.ApplyAsync(), configure: null);
        await using var context = Context();
        Assert.False(await context.JointChatSessions.AnyAsync(s => s.Id == id));
        Assert.False(await context.JointChatMessages.AnyAsync(m => m.SessionId == id));
    }

    [Fact]
    public async Task APageThatStaysOpenSeesItsPartnersChanges()
    {
        // Each open page keeps one scope (and DbContext) for its lifetime, as ScopedPage does.
        var (a, b, _) = await PairAsync(eligible: true);
        await using var pageA = db.BuildServices(clock, configure: JointChatPilot.On());
        await using var scopeA = pageA.CreateAsyncScope();
        var chatA = scopeA.ServiceProvider.GetRequiredService<IJointChatService>();

        var started = await chatA.StartAsync(a, JointChatPolicy.Version);
        Assert.Equal(JointChatState.WaitingForPartner, (await chatA.GetAsync(a)).State);
        Assert.True((await Chat(s => s.JoinAsync(b, started.Value.SessionId!.Value, JointChatPolicy.Version))).Succeeded);

        Assert.True((await chatA.SendAsync(a, started.Value.SessionId!.Value, "Hello")).Succeeded);
        Assert.True((await Chat(s => s.PauseAsync(b, started.Value.SessionId!.Value, 5))).Succeeded);
        Assert.True((await chatA.GetAsync(a)).Paused);
    }

    [Fact]
    public async Task NoChatTextOrModerationOutcomeReachesTheLogs()
    {
        var (a, b, _) = await PairAsync(eligible: true);
        var id = await ActiveChatAsync(a, b);
        using var logs = new CapturingLoggerProvider();
        await RunLogged<IJointChatService, Result<JointChatSendResult>>(s => s.SendAsync(a, id, "Private words about the weekend"), logs);
        await RunLogged<IJointChatService, Result<JointChatSendResult>>(s => s.SendAsync(b, id, "You never listen to me"), logs);
        await RunLogged<IJointChatService, Result<JointChatSendResult>>(s => s.SendAsync(b, id, "I want to die"), logs);

        Assert.NotEmpty(logs.Lines);
        Assert.DoesNotContain("Private words", logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("never listen", logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("want to die", logs.AllText, StringComparison.Ordinal);
    }

    private async Task<T> RunLogged<TService, T>(Func<TService, Task<T>> call, CapturingLoggerProvider logs)
        where TService : notnull
    {
        await using var provider = db.BuildServices(clock, logs, JointChatPilot.On());
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<Guid> ActiveChatAsync(Guid a, Guid b)
    {
        var started = await Chat(s => s.StartAsync(a, JointChatPolicy.Version));
        Assert.True(started.Succeeded, started.Error?.ToString());
        var id = started.Value.SessionId!.Value;
        Assert.True((await Chat(s => s.JoinAsync(b, id, JointChatPolicy.Version))).Succeeded);
        return id;
    }

    private async Task<(Guid A, Guid B, Guid WorkspaceId)> PairAsync(bool eligible)
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.CreateAsync(a, "Ours"));
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(s => s.CreateInvitationAsync(a, workspace.Value.Id));
        Assert.True((await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.AcceptInvitationAsync(b, invite.Value.Token))).Succeeded);
        if (eligible)
        {
            await using var context = Context();
            await JointChatPilot.MakeEligibleAsync(context, workspace.Value.Id, a, clock.UtcNow);
        }

        return (a, b, workspace.Value.Id);
    }

    private Task<T> Chat<T>(Func<IJointChatService, Task<T>> call, Action<IServiceCollection>? configure = null) =>
        Run(call, configure ?? JointChatPilot.On());

    private Task<T> Admin<T>(Func<IJointChatAdminService, Task<T>> call, Action<IServiceCollection>? configure = null) =>
        Run(call, configure ?? JointChatPilot.On());

    private CouplesDbContext Context() => db.BuildServices(clock).CreateScope().ServiceProvider.GetRequiredService<CouplesDbContext>();

    private async Task<T> Run<TService, T>(Func<TService, Task<T>> call, Action<IServiceCollection>? configure = null)
        where TService : notnull
    {
        await using var provider = db.BuildServices(clock, configure: configure ?? JointChatPilot.On());
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
