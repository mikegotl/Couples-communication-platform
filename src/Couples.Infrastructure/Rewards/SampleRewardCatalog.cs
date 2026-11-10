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

    public static async Task SeedIfEmptyAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        if (!scope.ServiceProvider.GetRequiredService<IOptions<RewardsOptions>>().Value.SeedSampleCatalog)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        if (await db.RewardOffers.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        foreach (var details in Offers)
        {
            db.RewardOffers.Add(RewardOffer.Create(details, now).Value);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
