using Couples.Domain.Common;
using Couples.Domain.Rewards;

namespace Couples.UnitTests.Rewards;

public sealed class ConnectionScoreTests
{
    private static readonly DateOnly Week = new(2026, 11, 2); // a Monday
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private readonly Guid a = Guid.NewGuid();
    private readonly Guid b = Guid.NewGuid();

    [Theory]
    [InlineData("2026-11-02T00:00:00Z", "2026-11-02")]
    [InlineData("2026-11-08T23:59:59Z", "2026-11-02")]
    [InlineData("2026-11-09T00:00:00Z", "2026-11-09")]
    [InlineData("2026-11-05T12:00:00+05:00", "2026-11-02")]
    public void WeeksStartOnMondayUtc(string moment, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), ConnectionScore.WeekStartOf(DateTimeOffset.Parse(moment, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void AWarmBackAndForthOnThreeDaysScoresFull()
    {
        var week = ConnectionScore.Score(Week,
        [
            Msg(0, a, warm: true),
            Msg(1, b, warm: true, reply: true),
            Msg(3, a, warm: true),
        ]);

        Assert.Equal(3, week.ConnectedDays);
        Assert.True(week.BackAndForth);
        Assert.Equal(100, week.Total);
    }

    [Fact]
    public void MessagingEveryDayOrManyTimesADayEarnsNothingExtra()
    {
        var threeDays = ConnectionScore.Score(Week, [Msg(0, a), Msg(2, b), Msg(4, a)]);
        var flood = ConnectionScore.Score(Week, Enumerable.Range(0, 7).SelectMany(d => Enumerable.Range(0, 10).Select(i => Msg(d, i % 2 == 0 ? a : b, hour: i))));

        Assert.Equal(threeDays.StayingInTouch, flood.StayingInTouch);
        Assert.Equal(threeDays.Total, flood.Total);
    }

    [Fact]
    public void KindWordsIsAShareSoMoreMessagesDontHelp()
    {
        var few = ConnectionScore.Score(Week, [Msg(0, a, warm: true), Msg(1, a)]);
        var many = ConnectionScore.Score(Week, [Msg(0, a, warm: true), Msg(0, a, warm: true, hour: 1), Msg(1, a), Msg(1, a, hour: 2)]);

        Assert.Equal(20, few.KindWords);
        Assert.Equal(few.KindWords, many.KindWords);
    }

    [Fact]
    public void OneSidedWeekWithoutRepliesGetsNoBothVoicesPoints()
    {
        var week = ConnectionScore.Score(Week, [Msg(0, a), Msg(2, a)]);

        Assert.False(week.BackAndForth);
        Assert.Equal(0, week.BothVoices);
    }

    [Fact]
    public void AQuietWeekScoresZeroAndMessagesOutsideTheWeekDontCount()
    {
        var week = ConnectionScore.Score(Week, [Msg(-1, a, warm: true), Msg(7, b, warm: true)]);

        Assert.Equal(0, week.Total);
        Assert.Equal(0, week.Messages);
    }

    [Fact]
    public void CreditsAreOnePerTenPointsWithABonusForGrowth()
    {
        var low = new WeekScore(Week, 1, false, 1, 0, 13, 0, 0);
        var high = new WeekScore(Week.AddDays(7), 3, true, 3, 3, 40, 20, 40);

        Assert.Equal(1, ConnectionScore.CreditsFor(low, null));
        Assert.Equal(10, ConnectionScore.CreditsFor(high, null));
        Assert.Equal(10 + ConnectionScore.GrowthBonus, ConnectionScore.CreditsFor(high, low));
        Assert.Equal(1, ConnectionScore.CreditsFor(low, high));
    }

    [Fact]
    public void LongestQuietStretchCountsWholeDaysIncludingUpToNow()
    {
        var now = Monday.AddDays(13);
        var messages = new[] { Msg(0, a), Msg(1, b), Msg(6, a) };

        // Days 2-5 are quiet (4 days), then days 7-13 (7 days, until now).
        Assert.Equal(7, ConnectionScore.LongestQuietStretch(messages, Monday, now));
        Assert.Equal(0, ConnectionScore.LongestQuietStretch([Msg(0, a), Msg(1, b)], Monday, Monday.AddDays(1)));
    }

    [Theory]
    [InlineData("Thank you for making dinner", true)]
    [InlineData("I really appreciate you listening", true)]
    [InlineData("Sorry I snapped earlier.", true)]
    [InlineData("I hear you, and that makes sense.", true)]
    [InlineData("Thinking of you today", true)]
    [InlineData("You’re right about the weekend", true)]
    [InlineData("I'm not happy about the dishes", false)]
    [InlineData("I don't appreciate that tone", false)]
    [InlineData("Can you pick up milk?", false)]
    [InlineData("The thanksgiving plans are set", false)]
    [InlineData("", false)]
    public void WarmLanguageIsRecognisedConservatively(string text, bool expected) =>
        Assert.Equal(expected, WarmLanguage.IsWarm(text));

    [Fact]
    public void NewAccountLooksBackAFewWeeksButNotBeforeThePairFormed()
    {
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero); // week of 16 Nov
        var longAgo = CoupleRewardAccount.Open(Guid.NewGuid(), now.AddDays(-200), now);
        var recent = CoupleRewardAccount.Open(Guid.NewGuid(), new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero), now);

        Assert.Equal(new DateOnly(2026, 10, 19), longAgo.NextWeekToScore);
        Assert.Equal(new DateOnly(2026, 11, 9), recent.NextWeekToScore);
    }

    [Fact]
    public void ANewPairStartsFromZeroAndNeverRescoresWeeks()
    {
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var account = CoupleRewardAccount.Open(Guid.NewGuid(), now.AddDays(-30), now);
        account.Earn(10, new DateOnly(2026, 11, 9), now);
        Assert.Equal(10, account.Balance);

        account.StartPair(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), now);

        Assert.Equal(0, account.Balance);
        Assert.Equal(new DateOnly(2026, 11, 16), account.NextWeekToScore);
    }

    [Fact]
    public void SpendingNeedsEnoughCredits()
    {
        var account = CoupleRewardAccount.Open(Guid.NewGuid(), Monday, Monday);
        account.Earn(5, Week, Monday);

        Assert.Equal(DomainError.Validation, account.Spend(6, Monday).Error);
        Assert.True(account.Spend(5, Monday).Succeeded);
        Assert.Equal(0, account.Balance);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "Leeds", true)]
    [InlineData("Leeds", "leeds ", true)]
    [InlineData("Leeds", "York", false)]
    [InlineData("Leeds", null, false)]
    public void OffersWithAnAreaShowOnlyThere(string? offerArea, string? coupleArea, bool expected)
    {
        var offer = RewardOffer.Create(new RewardOfferDetails("Dinner", "Bistro", "", offerArea, RewardKind.Restaurant, 40, false), Monday).Value;
        Assert.Equal(expected, offer.AvailableIn(coupleArea));
    }

    [Theory]
    [InlineData("", "Bistro", 40)]
    [InlineData("Dinner", "", 40)]
    [InlineData("Dinner", "Bistro", 0)]
    [InlineData("Dinner", "Bistro", 5000)]
    public void OffersNeedATitleABusinessAndASensibleCost(string title, string partner, int cost) =>
        Assert.Equal(DomainError.Validation, RewardOffer.Create(new RewardOfferDetails(title, partner, null, null, RewardKind.Restaurant, cost, false), Monday).Error);

    private static MessageSignal Msg(int day, Guid sender, bool warm = false, bool reply = false, int hour = 0) =>
        new(Monday.AddDays(day).AddHours(hour), sender, reply, warm);
}
