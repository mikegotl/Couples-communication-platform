using Couples.AI;
using Couples.Application.Coaching;
using Couples.Application.Messaging;
using Couples.Application.Workspaces;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Messaging;

/// <summary>
/// Milestone 07 gate: messages deliver only after affirmative approval of a previewed text;
/// duplicate submissions and revoked consent are safe; AI suggestions stay private to the author.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class DeliveryLifecycleTests(PostgresFixture db)
{
    [Fact]
    public async Task PreviewShowsExactTextAndDeliversNothing()
    {
        var (a, b, _) = await PairAsync("Our place");
        var text = $"Could we keep Sunday mornings for us? {Marker()}";
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, text));

        var preview = await Run<IMessagingService, Result<SharePreview>>(s => s.PreviewShareAsync(a, draft.Value.Id));

        Assert.True(preview.Succeeded);
        Assert.Equal(text, preview.Value.Body);
        Assert.Equal(draft.Value.Fingerprint, preview.Value.Fingerprint);
        Assert.Equal("Our place", preview.Value.WorkspaceName);
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));
        var context = await ContextAsync();
        Assert.False(await context.SharedMessages.AnyAsync(m => m.SourceDraftId == draft.Value.Id));
        Assert.False(await context.ConsentRecords.AnyAsync(c => c.SubjectId == draft.Value.Id));
    }

    [Fact]
    public async Task PreviewIsOwnerOnlyAndNeedsAPartner()
    {
        var (a, b, _) = await PairAsync();
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "Mine"));
        var solo = await db.CreateUserAsync();
        await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.CreateAsync(solo, "Solo"));
        var soloDraft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(solo, "Hello?"));

        Assert.Equal(DomainError.NotFound, (await Run<IMessagingService, Result<SharePreview>>(s => s.PreviewShareAsync(b, draft.Value.Id))).Error);
        Assert.Equal(DomainError.NoPartner, (await Run<IMessagingService, Result<SharePreview>>(s => s.PreviewShareAsync(solo, soloDraft.Value.Id))).Error);
    }

    [Fact]
    public async Task EditingAfterPreviewBlocksDeliveryUntilPreviewedAgain()
    {
        var (a, b, _) = await PairAsync();
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "First wording"));
        var preview = await Run<IMessagingService, Result<SharePreview>>(s => s.PreviewShareAsync(a, draft.Value.Id));
        await Run<IMessagingService, Result<DraftView>>(s => s.EditDraftAsync(a, draft.Value.Id, "Second wording"));

        var stale = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, preview.Value.Fingerprint));
        Assert.Equal(DomainError.ConsentMismatch, stale.Error);
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));

        var fresh = await Run<IMessagingService, Result<SharePreview>>(s => s.PreviewShareAsync(a, draft.Value.Id));
        var sent = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, fresh.Value.Fingerprint));
        Assert.True(sent.Succeeded);
        Assert.Equal("Second wording", Assert.Single(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b))).Body);
    }

    [Fact]
    public async Task RepeatedConfirmationReturnsTheOriginalDeliveryAndRecordsNothingNew()
    {
        var (a, b, _) = await PairAsync();
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "Just once"));

        var first = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint));
        var second = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint));
        var wrong = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, "not-the-approval"));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Equal(DomainError.Conflict, wrong.Error);
        Assert.Single(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));
        var context = await ContextAsync();
        Assert.Equal(1, await context.SharedMessages.CountAsync(m => m.SourceDraftId == draft.Value.Id));
        Assert.Equal(1, await context.ConsentRecords.CountAsync(c => c.SubjectId == draft.Value.Id));
        Assert.Equal(1, await context.AuditEvents.CountAsync(e => e.SubjectId == first.Value.Id && e.Action == AuditActions.MessageShared));
    }

    [Fact]
    public async Task ConfirmingAgainAfterWithdrawalNeverRedelivers()
    {
        var (a, b, _) = await PairAsync();
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "Take it back"));
        var sent = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint));
        await Run<IMessagingService, Result>(s => s.RevokeAsync(a, sent.Value.Id));

        var again = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint));

        // The repeat returns the original, still-withdrawn delivery; a new share needs a new draft and approval.
        Assert.True(again.Succeeded);
        Assert.Equal(sent.Value.Id, again.Value.Id);
        Assert.True(again.Value.Revoked);
        Assert.Equal(string.Empty, again.Value.Body);
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));
        var context = await ContextAsync();
        Assert.Equal(1, await context.SharedMessages.CountAsync(m => m.SourceDraftId == draft.Value.Id));
        Assert.NotNull((await context.ConsentRecords.SingleAsync(c => c.SubjectId == draft.Value.Id)).WithdrawnAt);
    }

    [Fact]
    public async Task SuggestionsAreForTheAuthorOnlyAndChangeNothing()
    {
        var (a, b, _) = await PairAsync();
        var text = "You never tell me when you'll be late.";
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, text));
        var logs = new CapturingLoggerProvider();

        var suggested = await Assist(s => s.SuggestRewriteAsync(a, draft.Value.Id), logs);
        var partner = await Assist(s => s.SuggestRewriteAsync(b, draft.Value.Id));

        Assert.True(suggested.Succeeded);
        Assert.Equal(CoachOutcome.Suggested, suggested.Value.Outcome);
        Assert.NotEmpty(suggested.Value.Suggestions);
        Assert.Equal(DomainError.NotFound, partner.Error);
        Assert.Equal(text, (await Run<IMessagingService, Result<DraftView>>(s => s.GetDraftAsync(a, draft.Value.Id))).Value.Body);
        Assert.Empty(await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(s => s.InboxAsync(b)));
        Assert.DoesNotContain(logs.Lines, l => l.Contains("late", StringComparison.OrdinalIgnoreCase));

        await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint));
        Assert.Equal(DomainError.Conflict, (await Assist(s => s.SuggestRewriteAsync(a, draft.Value.Id))).Error);
    }

    [Fact]
    public async Task DisabledCoachIsReportedUnavailable()
    {
        var (a, _, _) = await PairAsync();
        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "Hello"));

        await using var provider = db.BuildServices(configure: services => services.AddCoaching());
        await using var scope = provider.CreateAsyncScope();
        var assistant = scope.ServiceProvider.GetRequiredService<IDraftAssistant>();

        Assert.False(assistant.IsAvailable);
        Assert.Equal(CoachOutcome.Unavailable, (await assistant.SuggestRewriteAsync(a, draft.Value.Id)).Value.Outcome);
    }

    private static string Marker() => $"marker-{Guid.NewGuid():N}";

    private async Task<(Guid A, Guid B, Guid WorkspaceId)> PairAsync(string name = "Ours")
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.CreateAsync(a, name));
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(s => s.CreateInvitationAsync(a, workspace.Value.Id));
        Assert.True((await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.AcceptInvitationAsync(b, invite.Value.Token))).Succeeded);
        return (a, b, workspace.Value.Id);
    }

    private async Task<CouplesDbContext> ContextAsync()
    {
        await Task.CompletedTask;
        return db.BuildServices().CreateScope().ServiceProvider.GetRequiredService<CouplesDbContext>();
    }

    private async Task<Result<CoachResult>> Assist(Func<IDraftAssistant, Task<Result<CoachResult>>> call, CapturingLoggerProvider? logs = null)
    {
        await using var provider = db.BuildServices(logs: logs, configure: services =>
        {
            services.AddCoaching();
            services.Configure<CoachingOptions>(o => o.Provider = CoachProvider.Fake);
        });
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<IDraftAssistant>());
    }

    private async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> call)
        where TService : notnull
    {
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
