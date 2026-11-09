using System.Text.Json;
using Couples.Application.Messaging;
using Couples.Application.Privacy;
using Couples.Application.Reflections;
using Couples.Application.Workspaces;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.Consent;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Privacy;

/// <summary>
/// Milestone 03 gate: partner B cannot retrieve partner A's private data through service calls,
/// guessed ids, exports or logs, and shared content follows explicit per-message consent.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class PrivacyBoundaryTests(PostgresFixture db)
{
    [Fact]
    public async Task PartnerCannotReadChangeOrDeleteAnotherPartnersReflectionById()
    {
        var (a, b, _) = await PairAsync();
        var secret = Marker();
        var reflection = await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.CreateAsync(a, "Mine", secret));

        Assert.Equal(DomainError.NotFound, (await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.GetAsync(b, reflection.Value.Id))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.UpdateAsync(b, reflection.Value.Id, "x", "overwritten"))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IPrivateReflectionService, Result>(s => s.DeleteAsync(b, reflection.Value.Id))).Error);
        Assert.Empty(await Run<IPrivateReflectionService, IReadOnlyList<ReflectionView>>(s => s.ListAsync(b)));

        var unchanged = await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.GetAsync(a, reflection.Value.Id));
        Assert.Equal(secret, unchanged.Value.Body);
    }

    [Fact]
    public async Task PartnerCannotReadEditDiscardOrShareAnotherPartnersDraft()
    {
        var (a, b, _) = await PairAsync();
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, Marker()));

        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result<DraftView>>(s => s.GetDraftAsync(b, draft.Value.Id))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result<DraftView>>(s => s.EditDraftAsync(b, draft.Value.Id, "changed"))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result>(s => s.DiscardDraftAsync(b, draft.Value.Id))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(b, draft.Value.Id, draft.Value.Fingerprint))).Error);
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<DraftView>>(s => s.ListDraftsAsync(b)));
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));
    }

    [Fact]
    public async Task SharingRequiresConsentToTheExactPreviewedText()
    {
        var (a, b, _) = await PairAsync();
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "I felt hurt when plans changed."));
        var previewed = draft.Value.Fingerprint;
        await Run<IMessagingService, Result<DraftView>>(s => s.EditDraftAsync(a, draft.Value.Id, "Something different"));

        var stale = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, previewed));
        var missing = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, ""));

        Assert.Equal(DomainError.ConsentMismatch, stale.Error);
        Assert.Equal(DomainError.ConsentMismatch, missing.Error);
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));
    }

    [Fact]
    public async Task ApprovedShareReachesOnlyThePartnerAndIsRecordedInTheConsentLedger()
    {
        var (a, b, workspaceId) = await PairAsync();
        var outsider = await db.CreateUserAsync();
        var text = $"I'd like us to plan Sundays together. {Marker()}";
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, text));

        var shared = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint));

        Assert.True(shared.Succeeded);
        var inbox = await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b));
        Assert.Equal(text, Assert.Single(inbox).Body);
        Assert.Single(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.SentAsync(a)));
        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result<SharedMessageView>>(s => s.GetSharedAsync(outsider, shared.Value.Id))).Error);
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(outsider)));

        await using var scope = await ScopeAsync();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        var consent = await context.ConsentRecords.SingleAsync(c => c.SubjectId == draft.Value.Id);
        Assert.Equal(a, consent.UserId);
        Assert.Equal(ConsentPurpose.ShareMessage, consent.Purpose);
        Assert.Equal(draft.Value.Fingerprint, consent.ContentFingerprint);
        var audit = await context.AuditEvents.SingleAsync(e => e.SubjectId == shared.Value.Id);
        Assert.Equal(AuditActions.MessageShared, audit.Action);
        Assert.Equal(workspaceId, audit.WorkspaceId);
    }

    [Fact]
    public async Task ADraftCanBeSharedOnlyOnce()
    {
        var (a, b, _) = await PairAsync();
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "Once only"));

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint))));

        Assert.Equal(1, results.Count(r => r.Succeeded));
        Assert.Single(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));
    }

    [Fact]
    public async Task NothingCanBeSharedBeforeAPartnerJoins()
    {
        var a = await db.CreateUserAsync();
        await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.CreateAsync(a, "Solo"));
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "Hello?"));

        Assert.Equal(DomainError.NoPartner, (await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint))).Error);
    }

    [Fact]
    public async Task RevokingRemovesTheMessageFromTheRecipientAndErasesItsText()
    {
        var (a, b, _) = await PairAsync();
        var shared = await ShareAsync(a, "Please forget I said this");

        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result>(s => s.RevokeAsync(b, shared.Id))).Error);
        Assert.True((await Run<IMessagingService, Result>(s => s.RevokeAsync(a, shared.Id))).Succeeded);

        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));
        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result<SharedMessageView>>(s => s.GetSharedAsync(b, shared.Id))).Error);

        await using var scope = await ScopeAsync();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        var stored = await context.SharedMessages.SingleAsync(m => m.Id == shared.Id);
        Assert.Equal(string.Empty, stored.Body);
        Assert.NotNull((await context.ConsentRecords.SingleAsync(c => c.Id == stored.ConsentRecordId)).WithdrawnAt);
    }

    [Fact]
    public async Task RecipientWhoLeavesLosesAccessToSharedMessages()
    {
        var (a, b, workspaceId) = await PairAsync();
        var shared = await ShareAsync(a, Marker());

        await Run<IWorkspaceService, Result>(s => s.LeaveAsync(b, workspaceId));

        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));
        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result<SharedMessageView>>(s => s.GetSharedAsync(b, shared.Id))).Error);
        var export = await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(b));
        Assert.Empty(export.MessagesReceived);
    }

    [Fact]
    public async Task WhenTheSenderLeavesTheRecipientCanNoLongerReadTheirMessages()
    {
        var (a, b, workspaceId) = await PairAsync();
        var shared = await ShareAsync(a, Marker());

        await Run<IWorkspaceService, Result>(s => s.LeaveAsync(a, workspaceId));

        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result<SharedMessageView>>(s => s.GetSharedAsync(b, shared.Id))).Error);
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));
    }

    [Fact]
    public async Task MemberWhoLeftCannotShareOldDrafts()
    {
        var (a, _, workspaceId) = await PairAsync();
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "Written before leaving"));

        await Run<IWorkspaceService, Result>(s => s.LeaveAsync(a, workspaceId));

        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "new"))).Error);
    }

    [Fact]
    public async Task ExportContainsOnlyTheRequestersOwnPrivateData()
    {
        var (a, b, _) = await PairAsync();
        var aReflection = Marker();
        var aDraft = Marker();
        var bReflection = Marker();
        await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.CreateAsync(a, "A", aReflection));
        await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, aDraft));
        await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.CreateAsync(b, "B", bReflection));
        var sharedText = Marker();
        await ShareAsync(a, sharedText);

        var exportA = JsonSerializer.Serialize(await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(a)));
        var exportB = JsonSerializer.Serialize(await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(b)));

        Assert.Contains(aReflection, exportA, StringComparison.Ordinal);
        Assert.Contains(aDraft, exportA, StringComparison.Ordinal);
        Assert.DoesNotContain(bReflection, exportA, StringComparison.Ordinal);

        Assert.Contains(bReflection, exportB, StringComparison.Ordinal);
        Assert.DoesNotContain(aReflection, exportB, StringComparison.Ordinal);
        Assert.DoesNotContain(aDraft, exportB, StringComparison.Ordinal);

        // Shared text is the one thing B legitimately holds from A.
        Assert.Contains(sharedText, exportB, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrivateTextAndInviteTokensNeverReachLogs()
    {
        var logs = new CapturingLoggerProvider();
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var reflectionText = Marker();
        var draftText = Marker();
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.CreateAsync(a, "Logged space"), logs);
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(s => s.CreateInvitationAsync(a, workspace.Value.Id), logs);
        await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.AcceptInvitationAsync(b, invite.Value.Token), logs);
        await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.CreateAsync(a, "t", reflectionText), logs);
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, draftText), logs);
        var shared = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint), logs);
        await Run<IMessagingService, Result>(s => s.RevokeAsync(a, shared.Value.Id), logs);
        await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(b), logs);

        Assert.NotEmpty(logs.Lines);
        Assert.DoesNotContain(reflectionText, logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(draftText, logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(invite.Value.Token, logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("Logged space", logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletionErasesTheUsersDataButLeavesThePartnersIntact()
    {
        var (a, b, workspaceId) = await PairAsync();
        await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.CreateAsync(a, "A", Marker()));
        await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, Marker()));
        await ShareAsync(a, Marker());
        var bReflection = await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.CreateAsync(b, "B", Marker()));

        await Run<IPersonalDataService, Guid>(s => s.RequestDeletionAsync(a));
        Assert.True(await Run<IPersonalDataService, int>(s => s.ProcessPendingDeletionsAsync()) >= 1);

        await using var scope = await ScopeAsync();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        Assert.False(await context.Users.AnyAsync(u => u.Id == a));
        Assert.False(await context.PrivateReflections.AnyAsync(r => r.OwnerUserId == a));
        Assert.False(await context.DraftMessages.AnyAsync(d => d.OwnerUserId == a));
        Assert.False(await context.SharedMessages.AnyAsync(m => m.SenderUserId == a || m.RecipientUserId == a));
        Assert.False(await context.ConsentRecords.AnyAsync(c => c.UserId == a));
        Assert.False(await context.WorkspaceMemberships.AnyAsync(m => m.UserId == a));
        Assert.Equal(Couples.Domain.Privacy.DeletionStatus.Completed, (await context.DataDeletionRequests.SingleAsync(r => r.UserId == a)).Status);

        // B keeps their account, their private data and their workspace.
        Assert.True((await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.GetAsync(b, bReflection.Value.Id))).Succeeded);
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.GetAsync(b, workspaceId));
        Assert.True(workspace.Succeeded);
        Assert.False(workspace.Value.PartnerJoined);
    }

    private static string Marker() => $"marker-{Guid.NewGuid():N}";

    private async Task<SharedMessageView> ShareAsync(Guid sender, string text)
    {
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(sender, text));
        var shared = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(sender, draft.Value.Id, draft.Value.Fingerprint));
        Assert.True(shared.Succeeded);
        return shared.Value;
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

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        await Task.CompletedTask;
        return db.BuildServices().CreateAsyncScope();
    }

    /// <summary>Runs one call in its own scope, like one web request.</summary>
    private async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> call, CapturingLoggerProvider? logs = null)
        where TService : notnull
    {
        await using var provider = db.BuildServices(logs: logs);
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
