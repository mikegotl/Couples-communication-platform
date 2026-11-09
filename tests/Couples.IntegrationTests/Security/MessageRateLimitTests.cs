using Couples.Application.Messaging;
using Couples.Application.Workspaces;
using Couples.Domain.Common;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Security;

/// <summary>Milestone 10: a per-person hourly limit slows rapid escalation without losing anything.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class MessageRateLimitTests(PostgresFixture db)
{
    [Fact]
    public async Task SendingPausesAfterTheHourlyLimitAndResumesLater()
    {
        var clock = new FakeClock(new DateTimeOffset(2041, 3, 1, 9, 0, 0, TimeSpan.Zero));
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.CreateAsync(a, "Ours"));
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(clock, s => s.CreateInvitationAsync(a, workspace.Value.Id));
        await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.AcceptInvitationAsync(b, invite.Value.Token));

        for (var i = 0; i < MessagingLimits.SharesPerHour; i++)
        {
            var draft = await Run<IMessagingService, Result<DraftView>>(clock, s => s.CreateDraftAsync(a, $"Message {i}"));
            Assert.True((await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint))).Succeeded);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var extra = await Run<IMessagingService, Result<DraftView>>(clock, s => s.CreateDraftAsync(a, "One more"));
        Assert.Equal(DomainError.RateLimited, (await Run<IMessagingService, Result<SharePreview>>(clock, s => s.PreviewShareAsync(a, extra.Value.Id))).Error);
        Assert.Equal(DomainError.RateLimited, (await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(a, extra.Value.Id, extra.Value.Fingerprint))).Error);

        await using (var provider = db.BuildServices(clock))
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
            Assert.Equal(MessagingLimits.SharesPerHour, await context.SharedMessages.CountAsync(m => m.SenderUserId == a));
            Assert.False(await context.ConsentRecords.AnyAsync(c => c.SubjectId == extra.Value.Id));
        }

        // The partner isn't limited by the other person's sending.
        var fromB = await Run<IMessagingService, Result<DraftView>>(clock, s => s.CreateDraftAsync(b, "Reply"));
        Assert.True((await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(b, fromB.Value.Id, fromB.Value.Fingerprint))).Succeeded);

        // An hour after the first message, there's room again and the saved draft can go.
        clock.Advance(TimeSpan.FromMinutes(51));
        Assert.True((await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(a, extra.Value.Id, extra.Value.Fingerprint))).Succeeded);
    }

    private async Task<TResult> Run<TService, TResult>(FakeClock clock, Func<TService, Task<TResult>> call)
        where TService : notnull
    {
        await using var provider = db.BuildServices(clock);
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
