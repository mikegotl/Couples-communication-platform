using Couples.Application.Abstractions;
using Couples.Application.Rewards;
using Couples.Domain.Rewards;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Couples.Infrastructure.Rewards;

/// <summary>
/// Demonstration offers so the rewards flow can be tried before any real business signs up.
/// Added only when <c>Rewards:SeedSampleCatalog</c> is true and the catalog is empty. Every one is
/// marked as a sample, names no real business, and issues SAMPLE codes.
/// </summary>
public static class SampleRewardCatalog
{
    public static IReadOnlyList<RewardOfferDetails> Offers { get; } =
    [
        new("Coffee and cake for two", "Sample neighbourhood café", "A slow coffee date: two drinks and something sweet to share.", null, RewardKind.Restaurant, 20, true),
        new("Cinema night", "Sample local cinema", "Two tickets for any standard screening, plus a shared popcorn.", null, RewardKind.Experience, 30, true),
        new("Dinner for two", "Sample family bistro", "A two-course dinner for two from the set menu.", null, RewardKind.Restaurant, 45, true),
        new("Restaurant gift card", "Sample gift card provider", "A gift card you can spend at participating restaurants near you.", null, RewardKind.GiftCard, 50, true),
        new("Pottery class for two", "Sample craft studio", "A two-hour beginners' wheel class. Make something together.", null, RewardKind.Experience, 70, true),
    ];

    /// <summary>Starter date ideas that work anywhere and name no business. Admins add local ones.</summary>
    public static IReadOnlyList<DateIdeaDetails> Ideas { get; } =
    [
        new("Sunset walk and a flask", "Pick a route neither of you has walked, take something warm to drink, and leave your phones in your pockets.", null, DateIdeaKind.Outdoors, null),
        new("Cook something new together", "Choose a recipe neither of you has made. One of you chops, one of you stirs, then swap.", null, DateIdeaKind.AtHome, null),
        new("Dessert-only night out", "Skip dinner and go straight to somewhere that does great puddings. Order two and share.", null, DateIdeaKind.FoodAndDrink, null),
        new("Be tourists in your own town", "Visit the one local museum, viewpoint or landmark you've both never got round to.", null, DateIdeaKind.Culture, null),
        new("Board game rematch", "Dig out a game, or borrow one from a board game café, and play best of three.", null, DateIdeaKind.Activity, null),
        new("Morning market and coffee", "Wander a weekend market with a small budget each, and buy the other a surprise.", null, DateIdeaKind.FoodAndDrink, null),
    ];

    public static async Task SeedIfEmptyAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        if (!scope.ServiceProvider.GetRequiredService<IOptions<RewardsOptions>>().Value.SeedSampleCatalog)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        if (!await db.RewardOffers.AnyAsync(cancellationToken))
        {
            foreach (var details in Offers)
            {
                db.RewardOffers.Add(RewardOffer.Create(details, now).Value);
            }
        }

        if (!await db.DateIdeas.AnyAsync(cancellationToken))
        {
            foreach (var details in Ideas)
            {
                db.DateIdeas.Add(DateIdea.Create(details, now).Value);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
