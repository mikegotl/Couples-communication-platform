using Couples.Application.Workspaces;
using Couples.Domain.Common;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Workspaces;

/// <summary>
/// Workspace, invitation and membership rules exercised through the real service and database,
/// including the security properties the milestone gate requires.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class WorkspaceServiceTests(PostgresFixture db)
{
    [Fact]
    public async Task CreatorBecomesTheOnlyMember()
    {
        var a = await db.CreateUserAsync();

        var created = await Service(s => s.CreateAsync(a, "  Our space  "));

        Assert.True(created.Succeeded);
        Assert.Equal("Our space", created.Value.Name);
        Assert.False(created.Value.PartnerJoined);
        Assert.Equal(created.Value.Id, (await Service(s => s.GetMyWorkspaceAsync(a)))!.Id);
    }

    [Fact]
    public async Task TwoSeparateAccountsJoinOneWorkspaceThroughAnInvitation()
    {
        var (a, b, workspaceId) = await PairAsync();

        var asA = await Service(s => s.GetAsync(a, workspaceId));
        var asB = await Service(s => s.GetAsync(b, workspaceId));

        Assert.True(asA.Succeeded && asA.Value.PartnerJoined);
        Assert.True(asB.Succeeded && asB.Value.PartnerJoined);
        Assert.Null(asA.Value.PendingInvitation);
    }

    [Fact]
    public async Task UnrelatedAccountCannotSeeOrActOnAWorkspace()
    {
        var (a, _, workspaceId) = await PairAsync();
        var outsider = await db.CreateUserAsync();

        Assert.Null(await Service(s => s.GetMyWorkspaceAsync(outsider)));
        Assert.Equal(DomainError.NotFound, (await Service(s => s.GetAsync(outsider, workspaceId))).Error);
        Assert.Equal(DomainError.NotFound, (await Service(s => s.CreateInvitationAsync(outsider, workspaceId))).Error);
        Assert.Equal(DomainError.NotFound, (await Service(s => s.LeaveAsync(outsider, workspaceId))).Error);

        // The members are unaffected.
        Assert.True((await Service(s => s.GetAsync(a, workspaceId))).Succeeded);
    }

    [Fact]
    public async Task UnknownWorkspaceIdLooksTheSameAsSomeoneElsesWorkspace()
    {
        var (_, _, workspaceId) = await PairAsync();
        var outsider = await db.CreateUserAsync();

        var foreign = await Service(s => s.GetAsync(outsider, workspaceId));
        var missing = await Service(s => s.GetAsync(outsider, Guid.NewGuid()));

        Assert.Equal(missing.Error, foreign.Error);
    }

    [Fact]
    public async Task InvitationTokenCannotBeReused()
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var c = await db.CreateUserAsync();
        var workspace = await Service(s => s.CreateAsync(a, "Ours"));
        var invite = await Service(s => s.CreateInvitationAsync(a, workspace.Value.Id));

        Assert.True((await Service(s => s.AcceptInvitationAsync(b, invite.Value.Token))).Succeeded);

        var reuse = await Service(s => s.AcceptInvitationAsync(c, invite.Value.Token));
        Assert.Equal(DomainError.InvitationInvalid, reuse.Error);
        Assert.Null(await Service(s => s.GetMyWorkspaceAsync(c)));

        // The same user replaying their own accepted token gains nothing either.
        Assert.False((await Service(s => s.AcceptInvitationAsync(b, invite.Value.Token))).Succeeded);
    }

    [Fact]
    public async Task ExpiredInvitationIsRejected()
    {
        var clock = new FakeClock(db.Clock.UtcNow);
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Service(s => s.CreateAsync(a, "Ours"), clock);
        var invite = await Service(s => s.CreateInvitationAsync(a, workspace.Value.Id), clock);

        clock.Advance(TimeSpan.FromHours(72) + TimeSpan.FromSeconds(1));

        Assert.Equal(DomainError.InvitationInvalid, (await Service(s => s.PreviewInvitationAsync(b, invite.Value.Token), clock)).Error);
        Assert.Equal(DomainError.InvitationInvalid, (await Service(s => s.AcceptInvitationAsync(b, invite.Value.Token), clock)).Error);
    }

    [Fact]
    public async Task InviterCannotAcceptTheirOwnInvitation()
    {
        var a = await db.CreateUserAsync();
        var workspace = await Service(s => s.CreateAsync(a, "Ours"));
        var invite = await Service(s => s.CreateInvitationAsync(a, workspace.Value.Id));

        Assert.Equal(DomainError.CannotAcceptOwnInvitation, (await Service(s => s.AcceptInvitationAsync(a, invite.Value.Token))).Error);
    }

    [Fact]
    public async Task NewInvitationReplacesThePreviousOne()
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Service(s => s.CreateAsync(a, "Ours"));
        var first = await Service(s => s.CreateInvitationAsync(a, workspace.Value.Id));
        var second = await Service(s => s.CreateInvitationAsync(a, workspace.Value.Id));

        Assert.Equal(DomainError.InvitationInvalid, (await Service(s => s.AcceptInvitationAsync(b, first.Value.Token))).Error);
        Assert.True((await Service(s => s.AcceptInvitationAsync(b, second.Value.Token))).Succeeded);
    }

    [Fact]
    public async Task RevokedInvitationIsRejected()
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Service(s => s.CreateAsync(a, "Ours"));
        var invite = await Service(s => s.CreateInvitationAsync(a, workspace.Value.Id));

        Assert.True((await Service(s => s.RevokeInvitationAsync(a, workspace.Value.Id, invite.Value.Id))).Succeeded);

        Assert.Equal(DomainError.InvitationInvalid, (await Service(s => s.AcceptInvitationAsync(b, invite.Value.Token))).Error);
    }

    [Fact]
    public async Task MalformedOrGuessedTokensAreRejected()
    {
        var b = await db.CreateUserAsync();

        foreach (var token in new[] { "", "abc", new string('A', 43), "'; DROP TABLE couples.invitations; --" })
        {
            Assert.Equal(DomainError.InvitationInvalid, (await Service(s => s.AcceptInvitationAsync(b, token))).Error);
        }
    }

    [Fact]
    public async Task WorkspaceIsLimitedToTwoMembers()
    {
        var (a, _, workspaceId) = await PairAsync();

        Assert.Equal(DomainError.WorkspaceFull, (await Service(s => s.CreateInvitationAsync(a, workspaceId))).Error);
    }

    [Fact]
    public async Task UserInAWorkspaceCannotCreateOrJoinAnother()
    {
        var (a, b, _) = await PairAsync();
        var c = await db.CreateUserAsync();
        var other = await Service(s => s.CreateAsync(c, "Other"));
        var invite = await Service(s => s.CreateInvitationAsync(c, other.Value.Id));

        Assert.Equal(DomainError.AlreadyInWorkspace, (await Service(s => s.CreateAsync(a, "Second"))).Error);
        Assert.Equal(DomainError.AlreadyInWorkspace, (await Service(s => s.AcceptInvitationAsync(b, invite.Value.Token))).Error);
    }

    [Fact]
    public async Task MemberWhoLeavesLosesAccessImmediately()
    {
        var (a, b, workspaceId) = await PairAsync();

        Assert.True((await Service(s => s.LeaveAsync(b, workspaceId))).Succeeded);

        Assert.Equal(DomainError.NotFound, (await Service(s => s.GetAsync(b, workspaceId))).Error);
        Assert.Equal(DomainError.NotFound, (await Service(s => s.CreateInvitationAsync(b, workspaceId))).Error);
        Assert.Null(await Service(s => s.GetMyWorkspaceAsync(b)));

        var remaining = await Service(s => s.GetAsync(a, workspaceId));
        Assert.True(remaining.Succeeded);
        Assert.False(remaining.Value.PartnerJoined);

        // The remaining member can invite again, and the leaver is free to start over.
        Assert.True((await Service(s => s.CreateInvitationAsync(a, workspaceId))).Succeeded);
        Assert.True((await Service(s => s.CreateAsync(b, "Fresh start"))).Succeeded);
    }

    [Fact]
    public async Task LeavingRevokesInvitationsTheLeaverIssued()
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Service(s => s.CreateAsync(a, "Ours"));
        var invite = await Service(s => s.CreateInvitationAsync(a, workspace.Value.Id));

        await Service(s => s.LeaveAsync(a, workspace.Value.Id));

        Assert.Equal(DomainError.InvitationInvalid, (await Service(s => s.AcceptInvitationAsync(b, invite.Value.Token))).Error);
    }

    [Fact]
    public async Task ConcurrentAcceptsOfOneTokenAdmitExactlyOneUser()
    {
        var a = await db.CreateUserAsync();
        var workspace = await Service(s => s.CreateAsync(a, "Ours"));
        var invite = await Service(s => s.CreateInvitationAsync(a, workspace.Value.Id));
        var contenders = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            contenders.Add(await db.CreateUserAsync());
        }

        var results = await Task.WhenAll(contenders.Select(u => Service(s => s.AcceptInvitationAsync(u, invite.Value.Token))));

        Assert.Equal(1, results.Count(r => r.Succeeded));
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        Assert.Equal(2, await context.WorkspaceMemberships.CountAsync(m => m.WorkspaceId == workspace.Value.Id && m.LeftAt == null));
    }

    [Fact]
    public async Task OnlyTheTokenHashIsStored()
    {
        var a = await db.CreateUserAsync();
        var workspace = await Service(s => s.CreateAsync(a, "Ours"));
        var invite = await Service(s => s.CreateInvitationAsync(a, workspace.Value.Id));

        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        var stored = await context.Invitations.SingleAsync(i => i.Id == invite.Value.Id);

        Assert.NotEqual(invite.Value.Token, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.False(await context.Invitations.AnyAsync(i => i.TokenHash == invite.Value.Token));
    }

    private async Task<(Guid A, Guid B, Guid WorkspaceId)> PairAsync()
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Service(s => s.CreateAsync(a, "Ours"));
        var invite = await Service(s => s.CreateInvitationAsync(a, workspace.Value.Id));
        var accepted = await Service(s => s.AcceptInvitationAsync(b, invite.Value.Token));
        Assert.True(accepted.Succeeded);
        return (a, b, workspace.Value.Id);
    }

    /// <summary>Runs one call in its own scope, like one web request.</summary>
    private async Task<T> Service<T>(Func<IWorkspaceService, Task<T>> call, FakeClock? clock = null)
    {
        await using var provider = db.BuildServices(clock);
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<IWorkspaceService>());
    }
}
