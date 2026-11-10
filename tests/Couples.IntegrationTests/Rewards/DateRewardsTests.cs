using Couples.Application;
using Couples.Application.Abstractions;
using Couples.Application.Messaging;
using Couples.Application.Rewards;
using Couples.Application.Safety;
using Couples.Application.Workspaces;
using Couples.Domain.Common;
using Couples.Domain.Rewards;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Rewards;

/// <summary>
/// Shared connection score, credits and date rewards (ADR-0017): weeks are scored once, both
/// partners see the same thing, credits are spent safely, and a safety concern turns rewards off
/// without telling the partner why.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class DateRewardsTests(PostgresFixture db)
{
    // Monday 2 November 2026, 09:00 UTC.
    private static readonly DateTimeOffset WeekOne = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AWarmWeekEarnsSharedCreditsOnceAndEitherPartnerCanCashIn()
    {
        var clock = new FakeClock(WeekOne);
        var (a, b, workspaceId) = await PairAsync(clock);
        var area = $"Testtown {Guid.NewGuid():N}";
        var offerId = await AddOfferAsync(clock, area, cost: 10);

        // Week one: warm messages on three days with a reply. Week two: one short message.
        await SendAsync(clock, a, "Thank you for making dinner last night.");
        clock.Advance(TimeSpan.FromDays(1));
        await SendAsync(clock, b, "I loved it too. Thinking of you today.", reply: true);
        clock.Advance(TimeSpan.FromDays(2));
        await SendAsync(clock, a, "Sorry I was grumpy this morning.");
        clock.Advance(TimeSpan.FromDays(5)); // Monday of week two
        await SendAsync(clock, a, "Running late.");
        clock.Advance(TimeSpan.FromDays(7)); // Monday of week three

        var forA = await GetAsync(clock, a);
        var forB = await GetAsync(clock, b);
        Assert.NotNull(forA?.Rewards);
        Assert.Equal(100 / 10 + 13 / 10, forA.Rewards.Balance);
        Assert.Equal(forA.Rewards.Balance, forB!.Rewards!.Balance);
        Assert.Equal(forA.Score, forB.Score);
        Assert.Equal([100, 13], forA.RecentWeeks.Select(w => w.Total));
        Assert.Equal(2, await EarnedEntriesAsync(clock, workspaceId));

        // The local offer appears once the couple sets their area; then the suggestion points at it.
        Assert.DoesNotContain(forA.Rewards.Offers, o => o.Id == offerId);
        Assert.True((await Run<IConnectionProgressService, Result>(clock, s => s.SetAreaAsync(b, area))).Succeeded);
        var withArea = await GetAsync(clock, a);
        Assert.Contains(withArea!.Rewards!.Offers, o => o.Id == offerId);
        Assert.Equal(area, withArea.Rewards.Area);

        var redeemed = await Run<IConnectionProgressService, Result<RedemptionView>>(clock, s => s.RedeemAsync(a, offerId));
        Assert.True(redeemed.Succeeded, redeemed.Error?.ToString());
        Assert.StartsWith("BT-", redeemed.Value.VoucherCode, StringComparison.Ordinal);

        var afterB = await GetAsync(clock, b);
        Assert.Equal(1, afterB!.Rewards!.Balance);
        var voucher = Assert.Single(afterB.Rewards.Redemptions);
        Assert.False(voucher.RedeemedByMe);
        Assert.Equal(redeemed.Value.VoucherCode, voucher.VoucherCode);
        Assert.Equal(DomainError.Validation, (await Run<IConnectionProgressService, Result<RedemptionView>>(clock, s => s.RedeemAsync(b, offerId))).Error);

        // Loading again scores nothing twice.
        await GetAsync(clock, a);
        Assert.Equal(2, await EarnedEntriesAsync(clock, workspaceId));
    }

    [Fact]
    public async Task TwoPartnersCashingInAtOnceCannotOverspend()
    {
        var clock = new FakeClock(WeekOne);
        var (a, b, _) = await PairAsync(clock);
        var area = $"Testtown {Guid.NewGuid():N}";
        var first = await AddOfferAsync(clock, area, cost: 10);
        var second = await AddOfferAsync(clock, area, cost: 10);
        await SendAsync(clock, a, "Thank you, love.");
        clock.Advance(TimeSpan.FromDays(1));
        await SendAsync(clock, b, "Glad you liked it.", reply: true);
        clock.Advance(TimeSpan.FromDays(1));
        await SendAsync(clock, a, "Looking forward to Friday.");
        clock.Advance(TimeSpan.FromDays(6));
        Assert.True((await Run<IConnectionProgressService, Result>(clock, s => s.SetAreaAsync(a, area))).Succeeded);
        Assert.Equal(10, (await GetAsync(clock, a))!.Rewards!.Balance);

        var results = await Task.WhenAll(
            Run<IConnectionProgressService, Result<RedemptionView>>(clock, s => s.RedeemAsync(a, first)),
            Run<IConnectionProgressService, Result<RedemptionView>>(clock, s => s.RedeemAsync(b, second)));

        Assert.Single(results, r => r.Succeeded);
        Assert.Contains(results, r => r.Error is DomainError.Conflict or DomainError.Validation);
        Assert.Equal(0, (await GetAsync(clock, b))!.Rewards!.Balance);
    }

    [Fact]
    public async Task ASafetyConcernTurnsRewardsOffWithoutTellingThePartnerWhy()
    {
        var clock = new FakeClock(WeekOne);
        var (a, b, workspaceId) = await PairAsync(clock);
        var offerId = await AddOfferAsync(clock, area: null, cost: 5);
        await SendAsync(clock, a, "Thanks for today.");
        clock.Advance(TimeSpan.FromDays(1));
        await SendAsync(clock, b, "Thank you too.", reply: true);
        clock.Advance(TimeSpan.FromDays(7));

        await Run<ISafetyService, SafetyStatus>(clock, s => s.SetSafetyModeAsync(a, true));

        // The person who turned on safety mode sees no score and no nudges.
        var forA = await GetAsync(clock, a);
        Assert.False(forA!.ShowProgress);
        Assert.Null(forA.Rewards);
        Assert.Empty(forA.RecentWeeks);

        // The partner sees the score as usual but no rewards, exactly as if rewards were switched off.
        var forB = await GetAsync(clock, b);
        var flagOff = await GetAsync(clock, b, rewardsFlag: false);
        Assert.True(forB!.ShowProgress);
        Assert.Null(forB.Rewards);
        Assert.Equal(flagOff, forB, ProgressComparer.Instance);
        Assert.Equal(DomainError.NotFound, (await Run<IConnectionProgressService, Result<RedemptionView>>(clock, s => s.RedeemAsync(b, offerId))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IConnectionProgressService, Result>(clock, s => s.SetAreaAsync(b, "Anywhere"))).Error);
        Assert.Equal(0, await EarnedEntriesAsync(clock, workspaceId));

        // When the hold ends, rewards return.
        await Run<ISafetyService, SafetyStatus>(clock, s => s.SetSafetyModeAsync(a, false));
        Assert.NotNull((await GetAsync(clock, b))!.Rewards);
    }

    [Fact]
    public async Task WithTheFlagOffThereIsAScoreButNoCreditsOrRewards()
    {
        var clock = new FakeClock(WeekOne);
        var (a, b, workspaceId) = await PairAsync(clock);
        var offerId = await AddOfferAsync(clock, area: null, cost: 5);
        await SendAsync(clock, a, "Thank you.");
        clock.Advance(TimeSpan.FromDays(7));

        var view = await GetAsync(clock, b, rewardsFlag: false);
        Assert.True(view!.ShowProgress);
        Assert.Null(view.Rewards);
        Assert.Single(view.RecentWeeks);
        Assert.Equal(DomainError.NotFound, (await Run<IConnectionProgressService, Result<RedemptionView>>(clock, s => s.RedeemAsync(a, offerId), rewardsFlag: false)).Error);
        Assert.Equal(0, await EarnedEntriesAsync(clock, workspaceId));
    }

    [Fact]
    public async Task WithoutAPartnerThereIsNothingToShow()
    {
        var clock = new FakeClock(WeekOne);
        var solo = await db.CreateUserAsync();
        await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.CreateAsync(solo, "Mine"));

        Assert.Null(await GetAsync(clock, solo));
        Assert.Equal(DomainError.NoPartner, (await Run<IConnectionProgressService, Result<RedemptionView>>(clock, s => s.RedeemAsync(solo, Guid.NewGuid()))).Error);
    }

    [Fact]
    public async Task OnlyAdminsCanManageTheCatalog()
    {
        var clock = new FakeClock(WeekOne);
        var someone = await db.CreateUserAsync();
        var details = new RewardOfferDetails("Dinner", "Bistro", null, null, RewardKind.Restaurant, 40, false);

        Assert.Equal(DomainError.NotFound, (await Run<IRewardCatalogAdminService, Result<Guid>>(clock, s => s.CreateAsync(someone, details))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IRewardCatalogAdminService, Result<RewardCatalogAdminView>>(clock, s => s.GetAsync(someone))).Error);

        await using (var provider = db.BuildServices(clock))
        await using (var scope = provider.CreateAsyncScope())
        {
            await JointChatPilot.MakeAdminAsync(scope.ServiceProvider.GetRequiredService<CouplesDbContext>(), someone);
        }

        var created = await Run<IRewardCatalogAdminService, Result<Guid>>(clock, s => s.CreateAsync(someone, details));
        Assert.True(created.Succeeded);
        Assert.True((await Run<IRewardCatalogAdminService, Result>(clock, s => s.SetActiveAsync(someone, created.Value, false))).Succeeded);
        var view = await Run<IRewardCatalogAdminService, Result<RewardCatalogAdminView>>(clock, s => s.GetAsync(someone));
        Assert.False(view.Value.Offers.Single(o => o.Id == created.Value).IsActive);
    }

    private async Task<(Guid A, Guid B, Guid WorkspaceId)> PairAsync(FakeClock clock)
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.CreateAsync(a, "Ours"));
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(clock, s => s.CreateInvitationAsync(a, workspace.Value.Id));
        Assert.True((await Run<IWorkspaceService, Result<WorkspaceSummary>>(clock, s => s.AcceptInvitationAsync(b, invite.Value.Token))).Succeeded);
        return (a, b, workspace.Value.Id);
    }

    private async Task SendAsync(FakeClock clock, Guid sender, string text, bool reply = false)
    {
        Guid? inReplyTo = null;
        if (reply)
        {
            var inbox = await Run<IMessagingService, IReadOnlyList<SharedMessageView>>(clock, s => s.InboxAsync(sender));
            inReplyTo = inbox[0].Id;
        }

        Result<DraftView> draft = inReplyTo is { } id
            ? (await Run<Couples.Application.Inbox.IInboxService, Result<Guid>>(clock, s => s.StartReplyAsync(sender, id, text))) is { Succeeded: true } started
                ? await Run<IMessagingService, Result<DraftView>>(clock, s => s.GetDraftAsync(sender, started.Value))
                : DomainError.Validation
            : await Run<IMessagingService, Result<DraftView>>(clock, s => s.CreateDraftAsync(sender, text));
        Assert.True(draft.Succeeded);
        var shared = await Run<IMessagingService, Result<SharedMessageView>>(clock, s => s.ShareAsync(sender, draft.Value.Id, draft.Value.Fingerprint));
        Assert.True(shared.Succeeded, shared.Error?.ToString());
    }

    private async Task<Guid> AddOfferAsync(FakeClock clock, string? area, int cost)
    {
        await using var provider = db.BuildServices(clock);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        var offer = RewardOffer.Create(new RewardOfferDetails("Dinner for two", "Test bistro", "Two courses.", area, RewardKind.Restaurant, cost, false), clock.UtcNow).Value;
        context.RewardOffers.Add(offer);
        await context.SaveChangesAsync();
        return offer.Id;
    }

    private async Task<int> EarnedEntriesAsync(FakeClock clock, Guid workspaceId)
    {
        await using var provider = db.BuildServices(clock);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CouplesDbContext>().CreditEntries
            .CountAsync(e => e.WorkspaceId == workspaceId && e.Kind == CreditEntryKind.WeeklyEarned);
    }

    private Task<ConnectionProgress?> GetAsync(FakeClock clock, Guid userId, bool rewardsFlag = true) =>
        Run<IConnectionProgressService, ConnectionProgress?>(clock, s => s.GetAsync(userId), rewardsFlag);

    private async Task<TResult> Run<TService, TResult>(FakeClock clock, Func<TService, Task<TResult>> call, bool rewardsFlag = true)
        where TService : notnull
    {
        await using var provider = db.BuildServices(clock, configure: services =>
            services.AddSingleton<IFeatureFlags>(new Flags(rewardsFlag)));
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private sealed class Flags(bool rewards) : IFeatureFlags
    {
        public Task<bool> IsEnabledAsync(string flag, CancellationToken cancellationToken = default) =>
            Task.FromResult(rewards && flag == FeatureFlags.DateRewards);
    }

    /// <summary>Compares the parts of a dashboard a partner can see (records holding lists don't compare by value).</summary>
    private sealed class ProgressComparer : IEqualityComparer<ConnectionProgress?>
    {
        public static readonly ProgressComparer Instance = new();

        public bool Equals(ConnectionProgress? x, ConnectionProgress? y) =>
            x is not null && y is not null
            && x.Score == y.Score && x.PreviousScore == y.PreviousScore && x.ThisWeekSoFar == y.ThisWeekSoFar
            && x.RecentWeeks.SequenceEqual(y.RecentWeeks) && x.LongestQuietStretchDays == y.LongestQuietStretchDays
            && x.Tip == y.Tip && x.ShowProgress == y.ShowProgress && x.Rewards == y.Rewards;

        public int GetHashCode(ConnectionProgress? obj) => obj?.Score ?? 0;
    }
}
