using Couples.Application.Messaging;
using Couples.Application.Privacy;
using Couples.Application.Workspaces;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Privacy;

[Collection(PostgresTestGroup.Name)]
public sealed class RetentionTests(PostgresFixture db)
{
    [Fact]
    public async Task RetentionPurgesOnlyRecordsPastTheirRule()
    {
        var clock = new FakeClock(db.Clock.UtcNow);
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var keeper = await db.CreateUserAsync();

        // Workspace that will be abandoned, with an expired invitation and a discarded draft.
        var oldWorkspace = await Run<IWorkspaceService, Couples.Domain.Common.Result<WorkspaceSummary>>(clock, s => s.CreateAsync(a, "Old"));
        var oldInvite = await Run<IWorkspaceService, Couples.Domain.Common.Result<CreatedInvitation>>(clock, s => s.CreateInvitationAsync(a, oldWorkspace.Value.Id));
        var discarded = await Run<IMessagingService, Couples.Domain.Common.Result<DraftView>>(clock, s => s.CreateDraftAsync(a, "throw away"));
        await Run<IMessagingService, Couples.Domain.Common.Result>(clock, s => s.DiscardDraftAsync(a, discarded.Value.Id));
        await Run<IWorkspaceService, Couples.Domain.Common.Result>(clock, s => s.LeaveAsync(a, oldWorkspace.Value.Id));

        clock.Advance(TimeSpan.FromDays(31));

        // Recent activity that must survive.
        var liveWorkspace = await Run<IWorkspaceService, Couples.Domain.Common.Result<WorkspaceSummary>>(clock, s => s.CreateAsync(keeper, "Live"));
        var liveInvite = await Run<IWorkspaceService, Couples.Domain.Common.Result<CreatedInvitation>>(clock, s => s.CreateInvitationAsync(keeper, liveWorkspace.Value.Id));
        var recentDraftOwnerWorkspace = await Run<IWorkspaceService, Couples.Domain.Common.Result<WorkspaceSummary>>(clock, s => s.CreateAsync(b, "Recent"));
        var recentDiscard = await Run<IMessagingService, Couples.Domain.Common.Result<DraftView>>(clock, s => s.CreateDraftAsync(b, "recent"));
        await Run<IMessagingService, Couples.Domain.Common.Result>(clock, s => s.DiscardDraftAsync(b, recentDiscard.Value.Id));

        var report = await Run<IRetentionService, RetentionReport>(clock, s => s.ApplyAsync());

        Assert.True(report.EmptyWorkspaces >= 1);
        await using var provider = db.BuildServices(clock);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        Assert.False(await context.Workspaces.AnyAsync(w => w.Id == oldWorkspace.Value.Id));
        Assert.False(await context.Invitations.AnyAsync(i => i.Id == oldInvite.Value.Id));
        Assert.False(await context.DraftMessages.AnyAsync(d => d.Id == discarded.Value.Id));

        Assert.True(await context.Workspaces.AnyAsync(w => w.Id == liveWorkspace.Value.Id));
        Assert.True(await context.Workspaces.AnyAsync(w => w.Id == recentDraftOwnerWorkspace.Value.Id));
        Assert.True(await context.Invitations.AnyAsync(i => i.Id == liveInvite.Value.Id));
        Assert.True(await context.DraftMessages.AnyAsync(d => d.Id == recentDiscard.Value.Id));
    }

    private async Task<TResult> Run<TService, TResult>(FakeClock clock, Func<TService, Task<TResult>> call)
        where TService : notnull
    {
        await using var provider = db.BuildServices(clock);
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
