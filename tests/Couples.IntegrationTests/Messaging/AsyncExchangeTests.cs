using System.Text.Json;
using Couples.Application.Inbox;
using Couples.Application.Messaging;
using Couples.Application.Notifications;
using Couples.Application.Pace;
using Couples.Application.Privacy;
using Couples.Application.Workspaces;
using Couples.Domain.Common;
using Couples.Domain.Notifications;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Messaging;

/// <summary>
/// Milestone 08 gate: both partners can complete a safe asynchronous exchange; notifications
/// contain no confidential content; pace controls are respected; no read receipts.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class AsyncExchangeTests(PostgresFixture db)
{
    [Fact]
    public async Task PartnersCompleteAnExchangeWithPrivateNotesAndAReply()
    {
        var clock = new FakeClock(db.Clock.UtcNow);
        var (a, b) = await PairAsync(clock);
        var logs = new CapturingLoggerProvider();
        var original = $"Can we talk about holidays this weekend? {Marker()}";
        var notes = $"I felt defensive at first. {Marker()}";
        var reply = $"Yes, Saturday morning works for me. {Marker()}";

        var sent = await SendAsync(clock, a, original, logs: logs);

        // B sees a content-free notice and a new item.
        Assert.Equal(1, await Run<INotificationService, int>(clock, s => s.UnreadCountAsync(b)));
        var notice = Assert.Single(await Run<INotificationService, IReadOnlyList<NotificationView>>(clock, s => s.ListAsync(b)));
        Assert.Equal("Your partner shared a message with you.", notice.Text);
        var item = Assert.Single(await Run<IInboxService, IReadOnlyList<InboxItem>>(clock, s => s.ListAsync(b)));
        Assert.True(item.IsNew);

        // A's view of the sent message is identical before and after B opens it: no read receipt.
        var before = await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(clock, s => s.SentAsync(a));
        var opened = await Run<IInboxService, Result<ReceivedMessage>>(clock, s => s.OpenAsync(b, sent.Id), logs);
        var after = await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(clock, s => s.SentAsync(a));
        Assert.Equal(original, opened.Value.Body);
        Assert.Equal(before, after);
        Assert.False(Assert.Single(await Run<IInboxService, IReadOnlyList<InboxItem>>(clock, s => s.ListAsync(b))).IsNew);
        Assert.Equal(0, await Run<INotificationService, int>(clock, s => s.UnreadCountAsync(b)));

        // B's private notes stay with B.
        Assert.True((await Run<IInboxService, Result>(clock, s => s.SaveReflectionAsync(b, sent.Id, notes), logs)).Succeeded);
        Assert.Equal(notes, (await Run<IInboxService, Result<ReceivedMessage>>(clock, s => s.OpenAsync(b, sent.Id))).Value.MyReflection);
        Assert.Equal(DomainError.NotFound, (await Run<IInboxService, Result>(clock, s => s.SaveReflectionAsync(a, sent.Id, "sender can't"))).Error);

        // B replies through the same preview and approval path.
        clock.Advance(TimeSpan.FromHours(5));
        var draftId = (await Run<IInboxService, Result<Guid>>(clock, s => s.StartReplyAsync(b, sent.Id, reply), logs)).Value;
        Assert.Equal(draftId, (await Run<IInboxService, Result<Guid>>(clock, s => s.StartReplyAsync(b, sent.Id, "second start"))).Value);
        var preview = await Run<IMessagingService, Result<SharePreview>>(clock, s => s.PreviewShareAsync(b, draftId));
        var replied = await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(b, draftId, preview.Value.Fingerprint), logs);
        Assert.True(replied.Succeeded);

        var aNotice = Assert.Single(await Run<INotificationService, IReadOnlyList<NotificationView>>(clock, s => s.ListAsync(a)));
        Assert.Equal("Your partner replied to a message you shared.", aNotice.Text);
        var aItem = Assert.Single(await Run<IInboxService, IReadOnlyList<InboxItem>>(clock, s => s.ListAsync(a)));
        Assert.True(aItem.IsReplyToMe);
        var thread = await Run<IInboxService, Result<ReceivedMessage>>(clock, s => s.OpenAsync(a, replied.Value.Id));
        Assert.Equal(original, thread.Value.MyEarlierMessage);
        Assert.Equal(reply, thread.Value.Body);
        Assert.Null(thread.Value.MyReflection);
        Assert.True(Assert.Single(await Run<IInboxService, IReadOnlyList<InboxItem>>(clock, s => s.ListAsync(b))).IReplied);

        // A never sees B's notes: not in A's inbox, export or anywhere in the logs.
        var aExport = JsonSerializer.Serialize(await Run<IPersonalDataService, PersonalDataExport>(clock, s => s.ExportAsync(a)));
        Assert.DoesNotContain(notes, aExport, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Lines, l => l.Contains(original, StringComparison.Ordinal) || l.Contains(notes, StringComparison.Ordinal) || l.Contains(reply, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotificationsHoldNoMessageContentAnywhere()
    {
        var clock = new FakeClock(db.Clock.UtcNow);
        var (a, b) = await PairAsync(clock);
        var marker = Marker();
        var sent = await SendAsync(clock, a, $"Private words {marker}");

        // The stored row has metadata columns only.
        var context = Context(clock);
        var columns = context.Model.FindEntityType(typeof(Notification))!.GetProperties().Select(p => p.Name).Order().ToList();
        Assert.Equal(["CreatedAt", "Id", "Kind", "ReadAt", "RecipientUserId", "SubjectId", "UpdatedAt", "VisibleAt"], columns);
        Assert.Equal(sent.Id, (await context.Notifications.SingleAsync(n => n.RecipientUserId == b)).SubjectId);

        var views = JsonSerializer.Serialize(await Run<INotificationService, IReadOnlyList<NotificationView>>(clock, s => s.ListAsync(b)));
        var export = JsonSerializer.Serialize((await Run<IPersonalDataService, PersonalDataExport>(clock, s => s.ExportAsync(b))).Notifications);
        Assert.DoesNotContain(marker, views, StringComparison.Ordinal);
        Assert.DoesNotContain(marker, export, StringComparison.Ordinal);
        Assert.Contains(sent.Id.ToString(), export, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QuietHoursHoldTheNoticeButNotTheMessage()
    {
        var clock = new FakeClock(db.Clock.UtcNow); // 12:00 UTC
        var (a, b) = await PairAsync(clock);
        await Run<IPaceService, Result<PaceSettings>>(clock, s => s.SetQuietHoursAsync(b, new TimeOnly(11, 0), new TimeOnly(14, 0), "UTC"));

        await SendAsync(clock, a, "During quiet hours");

        Assert.Equal(0, await Run<INotificationService, int>(clock, s => s.UnreadCountAsync(b)));
        Assert.Empty(await Run<INotificationService, IReadOnlyList<NotificationView>>(clock, s => s.ListAsync(b)));
        Assert.Single(await Run<IInboxService, IReadOnlyList<InboxItem>>(clock, s => s.ListAsync(b)));

        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, await Run<INotificationService, int>(clock, s => s.UnreadCountAsync(b)));
    }

    [Fact]
    public async Task MuteSkipsNoticesButMessagesStillArrive()
    {
        var clock = new FakeClock(db.Clock.UtcNow);
        var (a, b) = await PairAsync(clock);
        await Run<IPaceService, Result<PaceSettings>>(clock, s => s.SetMutedAsync(b, true));

        await SendAsync(clock, a, "Quietly");

        Assert.False(await Context(clock).Notifications.AnyAsync(n => n.RecipientUserId == b));
        Assert.True(Assert.Single(await Run<IInboxService, IReadOnlyList<InboxItem>>(clock, s => s.ListAsync(b))).IsNew);
    }

    [Fact]
    public async Task PauseHoldsDeliveryAndOnlyThePauseIsVisibleToThePartner()
    {
        var clock = new FakeClock(db.Clock.UtcNow);
        var (a, b) = await PairAsync(clock);
        await Run<IPaceService, Result<PaceSettings>>(clock, s => s.SetQuietHoursAsync(b, new TimeOnly(22, 0), new TimeOnly(7, 0), "Europe/London"));
        var until = clock.UtcNow.AddDays(1);
        Assert.True((await Run<IPaceService, Result<PaceSettings>>(clock, s => s.PauseAsync(b, until))).Succeeded);

        var partnerView = await Run<IPaceService, PartnerPace?>(clock, s => s.GetPartnerPaceAsync(a));
        Assert.Equal(new PartnerPace(until), partnerView);

        var draft = await Run<IMessagingService, Result<DraftView>>(clock, s => s.CreateDraftAsync(a, "Held for later"));
        Assert.Equal(DomainError.PartnerPaused, (await Run<IMessagingService, Result<SharePreview>>(clock, s => s.PreviewShareAsync(a, draft.Value.Id))).Error);
        Assert.Equal(DomainError.PartnerPaused, (await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint))).Error);
        Assert.Empty(await Run<IInboxService, IReadOnlyList<InboxItem>>(clock, s => s.ListAsync(b)));
        Assert.False(await Context(clock).ConsentRecords.AnyAsync(c => c.SubjectId == draft.Value.Id));
        Assert.Equal("Draft", (await Run<IMessagingService, Result<DraftView>>(clock, s => s.GetDraftAsync(a, draft.Value.Id))).Value.Status);

        // The pause ends by itself.
        clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
        Assert.Equal(new PartnerPace(null), await Run<IPaceService, PartnerPace?>(clock, s => s.GetPartnerPaceAsync(a)));
        Assert.True((await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint))).Succeeded);
    }

    [Fact]
    public async Task WithdrawingRemovesTheNotice()
    {
        var clock = new FakeClock(db.Clock.UtcNow);
        var (a, b) = await PairAsync(clock);
        var sent = await SendAsync(clock, a, "Never mind");

        await Run<IMessagingService, Result>(clock, s => s.RevokeAsync(a, sent.Id));

        Assert.Empty(await Run<INotificationService, IReadOnlyList<NotificationView>>(clock, s => s.ListAsync(b)));
        Assert.Empty(await Run<IInboxService, IReadOnlyList<InboxItem>>(clock, s => s.ListAsync(b)));
        Assert.Equal(DomainError.NotFound, (await Run<IInboxService, Result<ReceivedMessage>>(clock, s => s.OpenAsync(b, sent.Id))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IInboxService, Result<Guid>>(clock, s => s.StartReplyAsync(b, sent.Id, "too late"))).Error);
    }

    [Fact]
    public async Task OutsidersAndSendersCannotUseTheRecipientsInbox()
    {
        var clock = new FakeClock(db.Clock.UtcNow);
        var (a, _) = await PairAsync(clock);
        var outsider = await db.CreateUserAsync();
        var sent = await SendAsync(clock, a, "For my partner only");

        foreach (var user in new[] { a, outsider })
        {
            Assert.Equal(DomainError.NotFound, (await Run<IInboxService, Result<ReceivedMessage>>(clock, s => s.OpenAsync(user, sent.Id))).Error);
            Assert.Equal(DomainError.NotFound, (await Run<IInboxService, Result<Guid>>(clock, s => s.StartReplyAsync(user, sent.Id, "x"))).Error);
            Assert.Equal(DomainError.NotFound, (await Run<IInboxService, Result>(clock, s => s.SaveReflectionAsync(user, sent.Id, "x"))).Error);
        }

        Assert.Null(await Run<IPaceService, PartnerPace?>(clock, s => s.GetPartnerPaceAsync(outsider)));
    }

    [Fact]
    public async Task DeletingAnAccountErasesItsPaceAndNoticesButKeepsThePartnersOwnNotes()
    {
        var clock = new FakeClock(db.Clock.UtcNow);
        var (a, b) = await PairAsync(clock);
        await Run<IPaceService, Result<PaceSettings>>(clock, s => s.SetMutedAsync(a, false));
        var sent = await SendAsync(clock, a, "Before I go");
        await Run<IInboxService, Result>(clock, s => s.SaveReflectionAsync(b, sent.Id, "My own notes"));

        await Run<IPersonalDataService, Guid>(clock, s => s.RequestDeletionAsync(a));
        await Run<IPersonalDataService, int>(clock, s => s.ProcessPendingDeletionsAsync());

        var context = Context(clock);
        Assert.False(await context.PacePreferences.AnyAsync(p => p.UserId == a));
        Assert.False(await context.Notifications.AnyAsync(n => n.SubjectId == sent.Id));
        var notes = await context.PrivateReflections.SingleAsync(r => r.OwnerUserId == b);
        Assert.Equal("My own notes", notes.Body);
        Assert.Null(notes.AboutMessageId);
    }

    private static string Marker() => $"marker-{Guid.NewGuid():N}";

    private async Task<SharedMessageView> SendAsync(FakeClock clock, Guid sender, string text, CapturingLoggerProvider? logs = null)
    {
        var draft = await Run<IMessagingService, Result<DraftView>>(clock, s => s.CreateDraftAsync(sender, text), logs);
        var shared = await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(sender, draft.Value.Id, draft.Value.Fingerprint), logs);
        Assert.True(shared.Succeeded, shared.Error?.ToString());
        return shared.Value;
    }

    private async Task<(Guid A, Guid B)> PairAsync(FakeClock clock)
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.CreateAsync(a, "Ours"));
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(clock, s => s.CreateInvitationAsync(a, workspace.Value.Id));
        Assert.True((await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.AcceptInvitationAsync(b, invite.Value.Token))).Succeeded);
        return (a, b);
    }

    private CouplesDbContext Context(FakeClock clock) => db.BuildServices(clock).CreateScope().ServiceProvider.GetRequiredService<CouplesDbContext>();

    private async Task<TResult> Run<TService, TResult>(FakeClock clock, Func<TService, Task<TResult>> call, CapturingLoggerProvider? logs = null)
        where TService : notnull
    {
        await using var provider = db.BuildServices(clock, logs);
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
