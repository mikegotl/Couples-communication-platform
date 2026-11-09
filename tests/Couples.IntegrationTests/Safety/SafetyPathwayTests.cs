using System.Text.Json;
using Couples.Application.Inbox;
using Couples.Application.Messaging;
using Couples.Application.Notifications;
using Couples.Application.Pace;
using Couples.Application.Privacy;
using Couples.Application.Safety;
using Couples.Application.Workspaces;
using Couples.Domain.Common;
using Couples.Domain.Safety;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Safety;

/// <summary>
/// Milestone 09 gate: safety scenarios block unsafe joint flows, and nothing about a user's
/// safety answers or safety mode reaches their partner, logs, audit events or notifications.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class SafetyPathwayTests(PostgresFixture db)
{
    private static Dictionary<string, string> Answers(string safe = "yes", string afraid = "no", string sayNo = "yes", string selfHarm = "no") => new()
    {
        ["safe-at-home"] = safe,
        ["afraid"] = afraid,
        ["can-say-no"] = sayNo,
        ["self-harm"] = selfHarm,
    };

    [Fact]
    public async Task UnsafeAnswersHoldTheUsersOwnSharingUntilTheyChangeThem()
    {
        var (a, b, _) = await PairAsync();
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "I need some space"));

        var status = await Run<ISafetyService, Result<SafetyStatus>>(s => s.RecordCheckAsync(a, Answers(afraid: "sometimes")));
        Assert.Equal(SafetyLevel.Unsafe, status.Value.Level);
        Assert.True(status.Value.SharingBlocked);

        Assert.Equal(DomainError.SafetyHold, (await Run<IMessagingService, Result<SharePreview>>(s => s.PreviewShareAsync(a, draft.Value.Id))).Error);
        Assert.Equal(DomainError.SafetyHold, (await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint))).Error);
        Assert.Empty(await Run<IInboxService, IReadOnlyList<InboxItem>>(s => s.ListAsync(b)));
        Assert.False(await Context().ConsentRecords.AnyAsync(c => c.SubjectId == draft.Value.Id));

        // The choice stays theirs: deleting the answers turns sharing back on.
        await Run<ISafetyService, SafetyStatus>(s => s.DeleteAnswersAsync(a));
        Assert.True((await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint))).Succeeded);
    }

    [Fact]
    public async Task ConcernAnswersShowSupportWithoutBlocking()
    {
        var (a, _, _) = await PairAsync();

        var status = await Run<ISafetyService, Result<SafetyStatus>>(s => s.RecordCheckAsync(a, Answers(safe: "unsure", selfHarm: "yes")));

        Assert.Equal(SafetyLevel.Concern, status.Value.Level);
        Assert.True(status.Value.SelfHarmSupport);
        Assert.False(status.Value.SharingBlocked);
        Assert.False(await Run<ISafetyGate, bool>(s => s.UserBlocksJointFlowsAsync(a)));
    }

    [Fact]
    public async Task SafetyModeIsInvisibleToThePartner()
    {
        var (a, b, workspaceId) = await PairAsync();
        var paceBefore = await Run<IPaceService, PartnerPace?>(s => s.GetPartnerPaceAsync(b));
        var workspaceBefore = await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.GetAsync(b, workspaceId));

        await Run<ISafetyService, SafetyStatus>(s => s.SetSafetyModeAsync(a, true));

        // B sends as usual and sees an ordinary delivery; nothing B can read has changed.
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(b, "How was your day?"));
        var preview = await Run<IMessagingService, Result<SharePreview>>(s => s.PreviewShareAsync(b, draft.Value.Id));
        var sent = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(b, draft.Value.Id, preview.Value.Fingerprint));
        Assert.True(sent.Succeeded);
        Assert.False(sent.Value.Revoked);
        Assert.Equal(paceBefore, await Run<IPaceService, PartnerPace?>(s => s.GetPartnerPaceAsync(b)));
        Assert.Equal(workspaceBefore.Value, (await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.GetAsync(b, workspaceId))).Value);
        var bExport = JsonSerializer.Serialize(await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(b)));
        Assert.Contains("\"Safety\":null", bExport, StringComparison.Ordinal);

        // A gets the message quietly: no notice, but it's there if A looks.
        Assert.Equal(0, await Run<INotificationService, int>(s => s.UnreadCountAsync(a)));
        Assert.False(await Context().Notifications.AnyAsync(n => n.RecipientUserId == a));
        Assert.Single(await Run<IInboxService, IReadOnlyList<InboxItem>>(s => s.ListAsync(a)));

        // A's own sharing is held.
        var aDraft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "Reply"));
        Assert.Equal(DomainError.SafetyHold, (await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, aDraft.Value.Id, aDraft.Value.Fingerprint))).Error);
    }

    [Fact]
    public async Task WorkspaceJointFlowsStopWhenEitherPartnerMayBeUnsafe()
    {
        var (a, b, workspaceId) = await PairAsync();
        Assert.True(await Run<ISafetyGate, bool>(s => s.WorkspaceAllowsJointFlowsAsync(workspaceId)));

        await Run<ISafetyService, Result<SafetyStatus>>(s => s.RecordCheckAsync(b, Answers(sayNo: "no")));
        Assert.False(await Run<ISafetyGate, bool>(s => s.WorkspaceAllowsJointFlowsAsync(workspaceId)));

        await Run<ISafetyService, SafetyStatus>(s => s.DeleteAnswersAsync(b));
        Assert.True(await Run<ISafetyGate, bool>(s => s.WorkspaceAllowsJointFlowsAsync(workspaceId)));

        await Run<ISafetyService, SafetyStatus>(s => s.SetSafetyModeAsync(a, true));
        Assert.False(await Run<ISafetyGate, bool>(s => s.WorkspaceAllowsJointFlowsAsync(workspaceId)));
    }

    [Fact]
    public async Task SafetyActivityLeavesNoTraceInLogsOrAudit()
    {
        var (a, _, _) = await PairAsync();
        var logs = new CapturingLoggerProvider();
        var auditBefore = await Context().AuditEvents.CountAsync(e => e.ActorUserId == a);

        await Run<ISafetyService, Result<SafetyStatus>>(s => s.RecordCheckAsync(a, Answers(safe: "no", afraid: "yes", selfHarm: "yes")), logs);
        await Run<ISafetyService, SafetyStatus>(s => s.SetSafetyModeAsync(a, true), logs);
        await Run<ISafetyService, SafetyStatus>(s => s.GetAsync(a), logs);

        Assert.Equal(auditBefore, await Context().AuditEvents.CountAsync(e => e.ActorUserId == a));
        // EF Core logs SQL (table and column names) but never parameter values; nothing else logs at all.
        Assert.DoesNotContain(logs.Lines, l => l.Contains("\"afraid\"", StringComparison.Ordinal) || l.Contains("Unsafe", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, l => !l.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            && (l.Contains("safety", StringComparison.OrdinalIgnoreCase) || l.Contains("self-harm", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task AnswersAreValidatedOwnedExportedAndErased()
    {
        var (a, b, _) = await PairAsync();
        Assert.Equal(DomainError.Validation, (await Run<ISafetyService, Result<SafetyStatus>>(s => s.RecordCheckAsync(a, new Dictionary<string, string> { ["safe-at-home"] = "yes" }))).Error);
        Assert.Equal(DomainError.Validation, (await Run<ISafetyService, Result<SafetyStatus>>(s => s.RecordCheckAsync(a, Answers(safe: "dunno")))).Error);

        await Run<ISafetyService, Result<SafetyStatus>>(s => s.RecordCheckAsync(a, Answers(afraid: "yes")));
        Assert.Null((await Run<ISafetyService, SafetyStatus>(s => s.GetAsync(b))).Level);

        var export = await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(a));
        Assert.Equal("yes", export.Safety!.Answers!["afraid"]);
        Assert.Equal("Unsafe", export.Safety.Level);

        await Run<IPersonalDataService, Guid>(s => s.RequestDeletionAsync(a));
        await Run<IPersonalDataService, int>(s => s.ProcessPendingDeletionsAsync());
        Assert.False(await Context().PersonalSafety.AnyAsync(s => s.UserId == a));
    }

    private async Task<(Guid A, Guid B, Guid WorkspaceId)> PairAsync()
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.CreateAsync(a, "Ours"));
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(s => s.CreateInvitationAsync(a, workspace.Value.Id));
        Assert.True((await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.AcceptInvitationAsync(b, invite.Value.Token))).Succeeded);
        return (a, b, workspace.Value.Id);
    }

    private CouplesDbContext Context() => db.BuildServices().CreateScope().ServiceProvider.GetRequiredService<CouplesDbContext>();

    private async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> call, CapturingLoggerProvider? logs = null)
        where TService : notnull
    {
        await using var provider = db.BuildServices(logs: logs);
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
