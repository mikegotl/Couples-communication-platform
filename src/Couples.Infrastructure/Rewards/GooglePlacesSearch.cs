using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Couples.Application.Abstractions;
using Couples.Application.Rewards;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Couples.Infrastructure.Rewards;

/// <summary>
/// Google Places API (New), Text Search. Sends only the search phrase and the town; asks only for
/// the name, address, type and Google Maps link (no ratings, photos or reviews, which cost more).
/// Results are held in memory briefly and never written to the database. Logs counts and status
/// codes only, never the town.
/// </summary>
internal sealed partial class GooglePlacesSearch(
    HttpClient http,
    IOptions<GooglePlacesOptions> options,
    IMemoryCache cache,
    IClock clock,
    GooglePlacesBudget budget,
    ILogger<GooglePlacesSearch> logger) : ILocalPlaceSearch
{
    public const string Endpoint = "https://places.googleapis.com/v1/places:searchText";
    private const string FieldMask = "places.displayName,places.formattedAddress,places.googleMapsUri,places.primaryType,places.primaryTypeDisplayName";

    private static readonly TimeSpan FailurePause = TimeSpan.FromMinutes(5);

    public bool Enabled => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<IReadOnlyList<LocalPlace>> SearchAsync(string area, int max, CancellationToken cancellationToken = default)
    {
        var town = area?.Trim();
        if (!Enabled || string.IsNullOrEmpty(town) || max <= 0)
        {
            return [];
        }

        var key = "places:" + town.ToUpperInvariant();
        if (cache.TryGetValue(key, out IReadOnlyList<LocalPlace>? cached) && cached is not null)
        {
            return [.. cached.Take(max)];
        }

        var o = options.Value;
        if (!budget.TrySpend(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), o.DailySearchCap))
        {
            LogCapReached(logger);
            return [];
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("X-Goog-Api-Key", o.ApiKey);
            request.Headers.Add("X-Goog-FieldMask", FieldMask);
            request.Content = JsonContent.Create(new SearchRequest($"{o.Query} in {town}", 10));
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, (int)response.StatusCode);
                cache.Set(key, (IReadOnlyList<LocalPlace>)[], FailurePause); // do not retry a failing lookup on every page view
                return [];
            }

            var body = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken);
            IReadOnlyList<LocalPlace> places = [.. (body?.Places ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.DisplayName?.Text) && IsGoogleMapsLink(p.GoogleMapsUri))
                .Select(p => new LocalPlace(Clip(p.DisplayName!.Text!, 80), Clip(p.FormattedAddress ?? string.Empty, 160), p.PrimaryTypeDisplayName?.Text, p.PrimaryType, p.GoogleMapsUri!))];
            if (o.CacheMinutes > 0)
            {
                cache.Set(key, places, TimeSpan.FromMinutes(Math.Min(o.CacheMinutes, 24 * 60)));
            }

            return [.. places.Take(max)];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            LogFailed(logger, 0);
            cache.Set(key, (IReadOnlyList<LocalPlace>)[], FailurePause);
            return [];
        }
    }

    /// <summary>Only links to Google's own Maps pages are shown, whatever the response says.</summary>
    private static bool IsGoogleMapsLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host is "maps.google.com" or "www.google.com" or "google.com" or "maps.app.goo.gl");

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Places lookup failed with status {Status}; showing curated date ideas only")]
    private static partial void LogFailed(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Places daily search cap reached; showing curated date ideas only")]
    private static partial void LogCapReached(ILogger logger);

    private sealed record SearchRequest([property: JsonPropertyName("textQuery")] string TextQuery, [property: JsonPropertyName("pageSize")] int PageSize);

    private sealed record SearchResponse([property: JsonPropertyName("places")] List<Place>? Places);

    private sealed record Place(
        [property: JsonPropertyName("displayName")] LocalizedText? DisplayName,
        [property: JsonPropertyName("formattedAddress")] string? FormattedAddress,
        [property: JsonPropertyName("googleMapsUri")] string? GoogleMapsUri,
        [property: JsonPropertyName("primaryType")] string? PrimaryType,
        [property: JsonPropertyName("primaryTypeDisplayName")] LocalizedText? PrimaryTypeDisplayName);

    private sealed record LocalizedText([property: JsonPropertyName("text")] string? Text);
}

/// <summary>Counts billable searches per UTC day for this app instance.</summary>
internal sealed class GooglePlacesBudget
{
    private readonly Lock gate = new();
    private DateOnly day;
    private int spent;

    public bool TrySpend(DateOnly today, int cap)
    {
        lock (gate)
        {
            if (today != day)
            {
                day = today;
                spent = 0;
            }

            if (spent >= cap)
            {
                return false;
            }

            spent++;
            return true;
        }
    }
}
