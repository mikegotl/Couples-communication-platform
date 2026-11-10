namespace Couples.Application.Rewards;

/// <summary>
/// Looks up real places for a date near a town (ADR-0019). The only thing sent to the provider is
/// the town or area the couple typed; never who they are, their score or anything they wrote.
/// </summary>
public interface ILocalPlaceSearch
{
    /// <summary>False until the provider is configured; callers then show curated ideas only.</summary>
    bool Enabled { get; }

    /// <summary>Up to <paramref name="max"/> places. Returns an empty list on any failure or when the daily cap is reached.</summary>
    Task<IReadOnlyList<LocalPlace>> SearchAsync(string area, int max, CancellationToken cancellationToken = default);
}

public sealed record LocalPlace(string Name, string Address, string? Category, string? PrimaryType, string MapsUrl);

/// <summary>Configuration under <c>Rewards:GooglePlaces</c>. No key, no lookups.</summary>
public sealed class GooglePlacesOptions
{
    public const string SectionName = "Rewards:GooglePlaces";

    /// <summary>A Google Maps Platform key with Places API (New) enabled. Kept in user secrets or Key Vault, never in the repository.</summary>
    public string? ApiKey { get; set; }

    /// <summary>What to search for; the couple's town is added after "in".</summary>
    public string Query { get; set; } = "date night spots";

    /// <summary>Billable searches allowed per UTC day by each app instance. Set a quota in Google Cloud as well.</summary>
    public int DailySearchCap { get; set; } = 200;

    /// <summary>How long one town's results are reused from memory before asking again; 0 asks every time. Nothing is stored in the database.</summary>
    public int CacheMinutes { get; set; } = 30;
}
