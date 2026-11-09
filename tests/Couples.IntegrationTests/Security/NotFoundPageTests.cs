using System.Net;

namespace Couples.IntegrationTests.Security;

/// <summary>An unknown address gets a real page: one heading, a specific title and a way home.</summary>
public sealed class NotFoundPageTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    [Fact]
    public async Task UnknownAddressShowsAHelpfulNotFoundPage()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/no-such-page", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<title>Page not found · BridgeTogether</title>", html, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<h1[ >]"));
        Assert.Contains("Go to the home page", html, StringComparison.Ordinal);
    }
}
