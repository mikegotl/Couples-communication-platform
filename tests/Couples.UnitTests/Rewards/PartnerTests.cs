using Couples.Domain.Common;
using Couples.Domain.Rewards;
using Couples.Domain.Workspaces;

namespace Couples.UnitTests.Rewards;

public sealed class PartnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static PartnerApplication Good => new("Luna Bistro", "Dana Reyes", "dana@lunabistro.example", null, "https://lunabistro.example", "Springfield", RewardKind.Restaurant, "A dessert to share with two mains.");

    [Fact]
    public void AGoodApplicationStartsPendingWithNoCheckLink()
    {
        var partner = RewardPartner.Apply(Good, Now).Value;

        Assert.Equal(PartnerStatus.Pending, partner.Status);
        Assert.Null(partner.CheckKeyHash);
    }

    [Theory]
    [InlineData("", "Dana", "dana@x.example", null, "Springfield", "Offer")]
    [InlineData("Luna", "", "dana@x.example", null, "Springfield", "Offer")]
    [InlineData("Luna", "Dana", "not-an-email", null, "Springfield", "Offer")]
    [InlineData("Luna", "Dana", "Dana <dana@x.example>", null, "Springfield", "Offer")]
    [InlineData("Luna", "Dana", "dana@x.example", "javascript:alert(1)", "Springfield", "Offer")]
    [InlineData("Luna", "Dana", "dana@x.example", null, "", "Offer")]
    [InlineData("Luna", "Dana", "dana@x.example", null, "Springfield", "")]
    public void IncompleteOrUnsafeApplicationsAreRefused(string business, string contact, string email, string? website, string area, string offer) =>
        Assert.Equal(DomainError.Validation, RewardPartner.Apply(new PartnerApplication(business, contact, email, null, website, area, RewardKind.Restaurant, offer), Now).Error);

    [Fact]
    public void ApprovingIssuesACheckKeyStoredOnlyAsAHashAndDecliningRemovesIt()
    {
        var partner = RewardPartner.Apply(Good, Now).Value;

        var key = partner.Approve(Now);
        Assert.Equal(PartnerStatus.Approved, partner.Status);
        Assert.Equal(InvitationToken.Hash(key), partner.CheckKeyHash);
        Assert.NotEqual(key, partner.CheckKeyHash);

        var replaced = partner.NewCheckKey(Now);
        Assert.NotEqual(key, replaced);
        Assert.Equal(InvitationToken.Hash(replaced), partner.CheckKeyHash);

        partner.Decline(Now);
        Assert.Null(partner.CheckKeyHash);
    }

    [Fact]
    public void APartnerOfferTakesItsNameAreaAndKindFromThePartnerAndIsNeverASample()
    {
        var partner = RewardPartner.Apply(Good, Now).Value;

        var offer = RewardOffer.CreateFor(partner, "Dessert to share", "With two mains.", 25, Now).Value;

        Assert.Equal(partner.Id, offer.PartnerId);
        Assert.Equal("Luna Bistro", offer.PartnerName);
        Assert.Equal("Springfield", offer.Area);
        Assert.False(offer.IsSample);
        Assert.Equal(DomainError.Validation, RewardOffer.CreateFor(partner, "", null, 25, Now).Error);
    }

    [Fact]
    public void AVoucherCanBeUsedOnceAndNotAfterItExpires()
    {
        var offer = RewardOffer.Create(new RewardOfferDetails("Dinner", "Bistro", null, null, RewardKind.Restaurant, 10, false), Now).Value;
        var pending = RewardRedemption.Reserve(Guid.NewGuid(), offer, Guid.NewGuid(), "Local", Now);
        Assert.False(pending.MarkUsed(Now));

        pending.Issue("BT-1", "Show it.", Now.AddDays(30), Now);
        Assert.True(pending.MarkUsed(Now.AddDays(1)));
        Assert.False(pending.MarkUsed(Now.AddDays(2)));

        var expired = RewardRedemption.Reserve(Guid.NewGuid(), offer, Guid.NewGuid(), "Local", Now);
        expired.Issue("BT-2", "Show it.", Now.AddDays(30), Now);
        Assert.False(expired.MarkUsed(Now.AddDays(31)));
    }

    [Theory]
    [InlineData("Picnic", "By the river.", null, true)]
    [InlineData("Picnic", "By the river.", "https://park.example", true)]
    [InlineData("Picnic", "By the river.", "http://park.example", false)]
    [InlineData("Picnic", "By the river.", "javascript:alert(1)", false)]
    [InlineData("", "By the river.", null, false)]
    [InlineData("Picnic", "", null, false)]
    public void DateIdeasNeedATitleADescriptionAndOnlyHttpsLinks(string title, string description, string? url, bool ok) =>
        Assert.Equal(ok, DateIdea.Create(new DateIdeaDetails(title, description, null, DateIdeaKind.Outdoors, url), Now).Succeeded);
}
