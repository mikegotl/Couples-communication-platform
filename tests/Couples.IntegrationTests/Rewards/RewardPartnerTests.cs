using Couples.Application;
using Couples.Application.Abstractions;
using Couples.Application.Messaging;
using Couples.Application.Privacy;
using Couples.Application.Rewards;
using Couples.Application.Workspaces;
using Couples.Domain.Common;
using Couples.Domain.Rewards;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Rewards;

/// <summary>
/// Reward partners and date ideas (ADR-0018): a business applies, an admin approves, couples in
/// that area can cash in, and the business confirms the code on its private check link.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class RewardPartnerTests(PostgresFixture db)
{
    private static readonly DateTimeOffset WeekOne = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ABusinessAppliesIsApprovedAndConfirmsACouplesVoucherOnce()
    {
        var clock = new FakeClock(WeekOne);
        var admin = await AdminAsync(clock);
        var town = $"Partnerville {Guid.NewGuid():N}";
        var application = Application(town);

        Assert.True((await Run<IPartnerService, Result>(clock, s => s.ApplyAsync(application))).Succeeded);
        Assert.True((await Run<IPartnerService, Result>(clock, s => s.ApplyAsync(application))).Succeeded); // sent twice: one row
        var pending = Assert.Single((await Run<IPartnerAdminService, Result<IReadOnlyList<AdminPartner>>>(clock, s => s.ListAsync(admin))).Value, p => p.Application.Area == town);
        Assert.Equal(PartnerStatus.Pending, pending.Status);

        // Nothing is shown to couples until approval.
        var (a, b) = await CoupleWithCreditsAsync(clock, town);
        Assert.DoesNotContain((await ProgressAsync(clock, a)).Rewards!.Offers, o => o.PartnerName == application.BusinessName);

        var key = (await Run<IPartnerAdminService, Result<string>>(clock, s => s.ApproveAsync(admin, pending.Id, "Dessert to share", "With two mains.", 10))).Value;
        Assert.Equal(DomainError.Conflict, (await Run<IPartnerAdminService, Result<string>>(clock, s => s.ApproveAsync(admin, pending.Id, "Again", null, 10))).Error);
        var offer = Assert.Single((await ProgressAsync(clock, a)).Rewards!.Offers, o => o.PartnerName == application.BusinessName);
        Assert.False(offer.IsSample);
        Assert.Equal(town, offer.Area);

        var voucher = (await Run<IConnectionProgressService, Result<RedemptionView>>(clock, s => s.RedeemAsync(a, offer.Id))).Value;

        // The business checks the code (typed in lower case), then marks it used, once.
        Assert.Equal(application.BusinessName, await Run<IPartnerService, string?>(clock, s => s.PartnerNameAsync(key)));
        var check = await Run<IPartnerService, VoucherCheck>(clock, s => s.CheckVoucherAsync(key, " " + voucher.VoucherCode.ToLowerInvariant()));
        Assert.Equal(VoucherState.Valid, check.State);
        Assert.Equal("Dessert to share", check.OfferTitle);
        Assert.Equal(VoucherState.Used, (await Run<IPartnerService, VoucherCheck>(clock, s => s.MarkUsedAsync(key, voucher.VoucherCode))).State);
        clock.Advance(TimeSpan.FromHours(1));
        var again = await Run<IPartnerService, VoucherCheck>(clock, s => s.MarkUsedAsync(key, voucher.VoucherCode));
        Assert.Equal(VoucherState.Used, again.State);
        Assert.Equal(clock.UtcNow.AddHours(-1), again.UsedAt);
        Assert.NotNull(Assert.Single((await ProgressAsync(clock, b)).Rewards!.Redemptions).UsedAt);

        // Wrong codes, a wrong link and another partner's link all look the same.
        Assert.Equal(VoucherState.NotFound, (await Run<IPartnerService, VoucherCheck>(clock, s => s.CheckVoucherAsync(key, "BT-00000000-00000000"))).State);
        Assert.Equal(VoucherState.NotFound, (await Run<IPartnerService, VoucherCheck>(clock, s => s.CheckVoucherAsync(new string('x', 43), voucher.VoucherCode))).State);
        var other = await ApprovedPartnerKeyAsync(clock, admin, $"Elsewhere {Guid.NewGuid():N}");
        Assert.Equal(VoucherState.NotFound, (await Run<IPartnerService, VoucherCheck>(clock, s => s.CheckVoucherAsync(other, voucher.VoucherCode))).State);

        // A new link replaces the old one; ending the partnership hides the offer and kills the link.
        var newKey = (await Run<IPartnerAdminService, Result<string>>(clock, s => s.NewCheckKeyAsync(admin, pending.Id))).Value;
        Assert.Null(await Run<IPartnerService, string?>(clock, s => s.PartnerNameAsync(key)));
        Assert.NotNull(await Run<IPartnerService, string?>(clock, s => s.PartnerNameAsync(newKey)));
        Assert.True((await Run<IPartnerAdminService, Result>(clock, s => s.DeclineAsync(admin, pending.Id))).Succeeded);
        Assert.Null(await Run<IPartnerService, string?>(clock, s => s.PartnerNameAsync(newKey)));
        Assert.DoesNotContain((await ProgressAsync(clock, a)).Rewards!.Offers, o => o.Id == offer.Id);
    }

    [Fact]
    public async Task OnlyAdminsSeeApplicationsAndBadOnesAreRefused()
    {
        var clock = new FakeClock(WeekOne);
        var someone = await db.CreateUserAsync();

        Assert.Equal(DomainError.NotFound, (await Run<IPartnerAdminService, Result<IReadOnlyList<AdminPartner>>>(clock, s => s.ListAsync(someone))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IPartnerAdminService, Result<string>>(clock, s => s.ApproveAsync(someone, Guid.NewGuid(), "x", null, 10))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IDateIdeaAdminService, Result<Guid>>(clock, s => s.CreateAsync(someone, new DateIdeaDetails("Picnic", "By the river.", null, DateIdeaKind.Outdoors, null)))).Error);
        Assert.Equal(DomainError.Validation, (await Run<IPartnerService, Result>(clock, s => s.ApplyAsync(Application("Town") with { ContactEmail = "nope" }))).Error);
    }

    [Fact]
    public async Task ApplicationsAreCappedPerDay()
    {
        var clock = new FakeClock(new DateTimeOffset(2031, 3, 3, 9, 0, 0, TimeSpan.Zero)); // a day no other test uses
        Task<Result> Apply(int i) => Run<IPartnerService, Result>(clock, s => s.ApplyAsync(Application("Capville") with { BusinessName = $"Place {i} {Guid.NewGuid():N}"[..30] }), capPerDay: 2);

        Assert.True((await Apply(1)).Succeeded);
        Assert.True((await Apply(2)).Succeeded);
        Assert.Equal(DomainError.RateLimited, (await Apply(3)).Error);
    }

    [Fact]
    public async Task DateIdeasShowLocalOnesFirstAndOnlyWhenRewardsAreAvailable()
    {
        var clock = new FakeClock(WeekOne);
        var admin = await AdminAsync(clock);
        var town = $"Ideaton {Guid.NewGuid():N}";
        var local = (await Run<IDateIdeaAdminService, Result<Guid>>(clock, s => s.CreateAsync(admin, new DateIdeaDetails("Riverside walk", "Flat path with a café at the end.", town, DateIdeaKind.Outdoors, "https://example.org/walk")))).Value;
        var elsewhere = (await Run<IDateIdeaAdminService, Result<Guid>>(clock, s => s.CreateAsync(admin, new DateIdeaDetails("Hill fort", "A climb with a view.", "Somewhere else " + town, DateIdeaKind.Outdoors, null)))).Value;
        var (a, _) = await CoupleWithCreditsAsync(clock, town);

        var ideas = (await ProgressAsync(clock, a)).Rewards!.Ideas;
        Assert.Equal(local, ideas[0].Id);
        Assert.DoesNotContain(ideas, i => i.Id == elsewhere);

        Assert.True((await Run<IDateIdeaAdminService, Result>(clock, s => s.SetActiveAsync(admin, local, false))).Succeeded);
        Assert.DoesNotContain((await ProgressAsync(clock, a)).Rewards!.Ideas, i => i.Id == local);
        Assert.Null((await ProgressAsync(clock, a, rewardsFlag: false)).Rewards);
    }

    [Fact]
    public async Task DeclinedApplicationsArePurgedAfterSixMonths()
    {
        var clock = new FakeClock(WeekOne);
        var admin = await AdminAsync(clock);
        var town = $"Purgeville {Guid.NewGuid():N}";
        await Run<IPartnerService, Result>(clock, s => s.ApplyAsync(Application(town)));
        var partner = (await Run<IPartnerAdminService, Result<IReadOnlyList<AdminPartner>>>(clock, s => s.ListAsync(admin))).Value.Single(p => p.Application.Area == town);
        await Run<IPartnerAdminService, Result>(clock, s => s.DeclineAsync(admin, partner.Id));

        clock.Advance(TimeSpan.FromDays(179));
        await Run<IRetentionService, RetentionReport>(clock, s => s.ApplyAsync());
        Assert.Contains((await Run<IPartnerAdminService, Result<IReadOnlyList<AdminPartner>>>(clock, s => s.ListAsync(admin))).Value, p => p.Id == partner.Id);

        clock.Advance(TimeSpan.FromDays(2));
        await Run<IRetentionService, RetentionReport>(clock, s => s.ApplyAsync());
        Assert.DoesNotContain((await Run<IPartnerAdminService, Result<IReadOnlyList<AdminPartner>>>(clock, s => s.ListAsync(admin))).Value, p => p.Id == partner.Id);
    }

    private static PartnerApplication Application(string town) =>
        new($"Luna {Guid.NewGuid():N}"[..20], "Dana Reyes", "dana@luna.example", "555 0100", "https://luna.example", town, RewardKind.Restaurant, "A dessert to share with two mains.");

    private async Task<string> ApprovedPartnerKeyAsync(FakeClock clock, Guid admin, string town)
    {
        await Run<IPartnerService, Result>(clock, s => s.ApplyAsync(Application(town)));
        var partner = (await Run<IPartnerAdminService, Result<IReadOnlyList<AdminPartner>>>(clock, s => s.ListAsync(admin))).Value.Single(p => p.Application.Area == town);
        return (await Run<IPartnerAdminService, Result<string>>(clock, s => s.ApproveAsync(admin, partner.Id, "Offer", null, 10))).Value;
    }

    private async Task<Guid> AdminAsync(FakeClock clock)
    {
        var admin = await db.CreateUserAsync();
        await using var provider = db.BuildServices(clock);
        await using var scope = provider.CreateAsyncScope();
        await JointChatPilot.MakeAdminAsync(scope.ServiceProvider.GetRequiredService<CouplesDbContext>(), admin);
        return admin;
    }

    /// <summary>A couple in <paramref name="town"/> with 10 credits, at the start of the following week.</summary>
    private async Task<(Guid A, Guid B)> CoupleWithCreditsAsync(FakeClock clock, string town)
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.CreateAsync(a, "Ours"));
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(clock, s => s.CreateInvitationAsync(a, workspace.Value.Id));
        Assert.True((await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.AcceptInvitationAsync(b, invite.Value.Token))).Succeeded);
        foreach (var (sender, text) in new[] { (a, "Thank you for today."), (b, "Thanks, I loved it."), (a, "Looking forward to Friday.") })
        {
            var draft = await Run<IMessagingService, Result<DraftView>>(clock, s => s.CreateDraftAsync(sender, text));
            Assert.True((await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(sender, draft.Value.Id, draft.Value.Fingerprint))).Succeeded);
            clock.Advance(TimeSpan.FromDays(1));
        }

        clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromDays(3));
        Assert.True((await Run<IConnectionProgressService, Result>(clock, s => s.SetAreaAsync(a, town))).Succeeded);
        Assert.Equal(10, (await ProgressAsync(clock, a)).Rewards!.Balance);
        return (a, b);
    }

    private async Task<ConnectionProgress> ProgressAsync(FakeClock clock, Guid userId, bool rewardsFlag = true) =>
        (await Run<IConnectionProgressService, ConnectionProgress?>(clock, s => s.GetAsync(userId), rewardsFlag))!;

    private async Task<TResult> Run<TService, TResult>(FakeClock clock, Func<TService, Task<TResult>> call, bool rewardsFlag = true, int? capPerDay = null)
        where TService : notnull
    {
        await using var provider = db.BuildServices(clock, configure: services =>
        {
            services.AddSingleton<IFeatureFlags>(new Flags(rewardsFlag));
            services.Configure<RewardsOptions>(o => o.PartnerApplicationsPerDay = capPerDay ?? 100_000);
        });
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private sealed class Flags(bool rewards) : IFeatureFlags
    {
        public Task<bool> IsEnabledAsync(string flag, CancellationToken cancellationToken = default) =>
            Task.FromResult(rewards && flag == FeatureFlags.DateRewards);
    }
}
