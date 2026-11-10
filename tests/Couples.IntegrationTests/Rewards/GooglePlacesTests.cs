using System.Net;
using System.Text;
using Couples.Application.Rewards;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Rewards;

/// <summary>
/// Local date spots from Google Places (ADR-0019): off without a key, sends only the town, is
/// capped per day, reuses recent answers, and falls back to curated ideas on any failure.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class GooglePlacesTests(PostgresFixture db)
{
    private const string TwoPlaces = """
        {"places":[
          {"displayName":{"text":"Harbour Kitchen"},"formattedAddress":"1 Quay St","googleMapsUri":"https://maps.google.com/?cid=1","primaryType":"seafood_restaurant","primaryTypeDisplayName":{"text":"Seafood restaurant"}},
          {"displayName":{"text":"Botanic Garden"},"formattedAddress":"2 Park Rd","googleMapsUri":"https://www.google.com/maps/place/?q=place_id:abc","primaryType":"botanical_garden"},
          {"displayName":{"text":"Odd link"},"formattedAddress":"3 Nowhere","googleMapsUri":"https://evil.example/maps"},
          {"displayName":{"text":"Plain http"},"formattedAddress":"4 Nowhere","googleMapsUri":"http://maps.google.com/?cid=4"}
        ]}
        """;

    [Fact]
    public async Task WithoutAKeyNothingIsSentAndIdeasAreCuratedOnly()
    {
        var handler = new StubHandler(TwoPlaces);
        await using var provider = Build(handler, apiKey: null);
        await using var scope = provider.CreateAsyncScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<ILocalPlaceSearch>().Enabled);
        var ideas = await scope.ServiceProvider.GetRequiredService<IDateIdeaProvider>().ListAsync("Harbourtown", 9, CancellationToken.None);
        Assert.DoesNotContain(ideas, i => i.FromGoogleMaps);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendsOnlyTheTownAndShowsPlacesWithGoogleMapsLinks()
    {
        var handler = new StubHandler(TwoPlaces);
        var logs = new CapturingLoggerProvider();
        var town = $"Harbourtown {Guid.NewGuid():N}";
        await using var provider = Build(handler, logs: logs);
        await using var scope = provider.CreateAsyncScope();

        var ideas = await scope.ServiceProvider.GetRequiredService<IDateIdeaProvider>().ListAsync($"  {town} ", 9, CancellationToken.None);

        var places = ideas.Where(i => i.FromGoogleMaps).ToList();
        Assert.Equal(["Harbour Kitchen", "Botanic Garden"], places.Select(p => p.Title));
        Assert.Equal("Seafood restaurant · 1 Quay St", places[0].Description);
        Assert.Equal(Couples.Domain.Rewards.DateIdeaKind.FoodAndDrink, places[0].Kind);
        Assert.Equal(Couples.Domain.Rewards.DateIdeaKind.Outdoors, places[1].Kind);
        Assert.All(places, p => Assert.StartsWith("https://", p.Url, StringComparison.Ordinal));
        Assert.All(places, p => Assert.Equal(town, p.Area));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://places.googleapis.com/v1/places:searchText", request.Url);
        Assert.Equal("test-key", request.Key);
        Assert.DoesNotContain("rating", request.FieldMask, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("photo", request.FieldMask, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($$"""{"textQuery":"date night spots in {{town}}","pageSize":10}""", request.Body);
        Assert.DoesNotContain(town, logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("test-key", logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecentAnswersAreReusedAndTheDailyCapStopsNewSearches()
    {
        var handler = new StubHandler(TwoPlaces);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        await using var provider = Build(handler, clock: clock, cap: 2);
        await using var scope = provider.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<ILocalPlaceSearch>();
        var suffix = Guid.NewGuid().ToString("N");

        Assert.Equal(2, (await search.SearchAsync("Aton " + suffix, 5)).Count);
        Assert.Single(await search.SearchAsync("ATON " + suffix, 1)); // same town, from memory
        Assert.Single(handler.Requests);

        Assert.Equal(2, (await search.SearchAsync("Bton " + suffix, 5)).Count);
        Assert.Empty(await search.SearchAsync("Cton " + suffix, 5)); // cap of 2 reached
        Assert.Equal(2, handler.Requests.Count);

        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(2, (await search.SearchAsync("Cton " + suffix, 5)).Count);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "{}")]
    public async Task AFailedLookupFallsBackToCuratedIdeas(HttpStatusCode status, string body)
    {
        await using var provider = Build(new StubHandler(body, status));
        await using var scope = provider.CreateAsyncScope();

        var ideas = await scope.ServiceProvider.GetRequiredService<IDateIdeaProvider>().ListAsync($"Quietville {Guid.NewGuid():N}", 9, CancellationToken.None);

        Assert.DoesNotContain(ideas, i => i.FromGoogleMaps);
    }

    [Fact]
    public async Task ANetworkErrorFallsBackToo()
    {
        await using var provider = Build(new StubHandler(TwoPlaces, fail: true));
        await using var scope = provider.CreateAsyncScope();

        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ILocalPlaceSearch>().SearchAsync($"Offline {Guid.NewGuid():N}", 5));
    }

    private ServiceProvider Build(StubHandler handler, string? apiKey = "test-key", FakeClock? clock = null, CapturingLoggerProvider? logs = null, int cap = 100) =>
        db.BuildServices(clock, logs, services =>
        {
            services.Configure<GooglePlacesOptions>(o =>
            {
                o.ApiKey = apiKey;
                o.DailySearchCap = cap;
            });
            services.AddHttpClient(nameof(ILocalPlaceSearch)).ConfigurePrimaryHttpMessageHandler(() => handler);
        });

    private sealed record Sent(string Url, string? Key, string FieldMask, string Body);

    private sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK, bool fail = false) : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Sent(
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("X-Goog-Api-Key", out var key) ? key.Single() : null,
                request.Headers.TryGetValues("X-Goog-FieldMask", out var mask) ? mask.Single() : string.Empty,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            if (fail)
            {
                throw new HttpRequestException("no network");
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
