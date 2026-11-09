using System.Net;

namespace Couples.IntegrationTests.Security;

/// <summary>Milestone 10: response headers, cookies, rate limits and request limits over HTTP.</summary>
public sealed class HttpHardeningTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/support")]
    [InlineData("/Account/Login")]
    public async Task PagesSendSecurityHeaders(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csp = Header(response, "Content-Security-Policy");
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
        Assert.Matches("script-src 'self' 'nonce-[a-f0-9]{32}';", csp);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"), StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task InlineImportMapCarriesTheResponseNonce()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();
        var nonce = System.Text.RegularExpressions.Regex.Match(Header(response, "Content-Security-Policy"), "'nonce-([^']+)'").Groups[1].Value;

        Assert.Contains($"nonce=\"{nonce}\"", html, StringComparison.Ordinal);
        // Every other script is an external file from this site.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(html, "<script(?![^>]*\\ssrc=)[^>]*>"));
    }

    [Fact]
    public async Task EachResponseGetsAFreshNonce()
    {
        using var client = factory.CreateClient();
        using var first = await client.GetAsync(new Uri("/", UriKind.Relative));
        using var second = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.NotEqual(Header(first, "Content-Security-Policy"), Header(second, "Content-Security-Policy"));
    }

    [Fact]
    public async Task CookiesAreHostOnlySecureAndHttpOnlyInProduction()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/Account/Login", UriKind.Relative));

        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];
        var antiforgery = Assert.Single(cookies, c => c.StartsWith("__Host-bt-af=", StringComparison.Ordinal));
        Assert.Contains("secure", antiforgery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", antiforgery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", antiforgery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", antiforgery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountFormPostsAreRateLimitedPerClient()
    {
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimits:AccountPostsPerMinute", "3"));
        using var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Email"] = "x@example.test", ["Input.Password"] = "wrong" });
            using var response = await client.PostAsync(new Uri("/Account/Login", UriKind.Relative), content);
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.True(response.Headers.RetryAfter is not null);
            }
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses.Take(3));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[3]);

        // Health checks are never limited.
        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task FormPostWithoutAntiforgeryTokenIsRejected()
    {
        using var client = factory.CreateClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Email"] = "x@example.test", ["Input.Password"] = "wrong" });
        using var response = await client.PostAsync(new Uri("/Account/Login", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OversizedBodiesAreRejected()
    {
        using var client = factory.CreateClient();
        using var content = new ByteArrayContent(new byte[1_100_000]);
        content.Headers.ContentType = new("application/x-www-form-urlencoded");
        using var response = await client.PostAsync(new Uri("/Account/Register", UriKind.Relative), content);

        Assert.True(response.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest, response.StatusCode.ToString());
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values)
        : response.Content.Headers.TryGetValues(name, out var content) ? string.Join(", ", content) : string.Empty;
}
