using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Couples.Infrastructure.Identity;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Couples.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Security;

internal static class ExternalSignInSettings
{
    public static IWebHostBuilder WithGoogleAndApple(this IWebHostBuilder builder) => builder
        .UseSetting("Authentication:Google:ClientId", "test-google-client")
        .UseSetting("Authentication:Google:ClientSecret", "test-google-secret")
        .UseSetting("Authentication:Apple:ClientId", "test.apple.services")
        .UseSetting("Authentication:Apple:TeamId", "TEAM123456")
        .UseSetting("Authentication:Apple:KeyId", "KEY1234567")
        .UseSetting("Authentication:Apple:PrivateKey", "not-used-until-a-code-exchange");

    public static string AntiforgeryToken(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
}

/// <summary>Buttons, CSP and the redirect to each provider, with and without settings.</summary>
public sealed class ExternalSignInHttpTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task WithoutSettingsThereAreNoButtonsAndNoExtraFormTargets(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Continue with", html, StringComparison.Ordinal);
        Assert.DoesNotContain("external-divider", html, StringComparison.Ordinal);
        Assert.DoesNotContain("no external authentication services", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("form-action 'self';", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task WithSettingsBothButtonsShowAndTheCspAllowsTheirSignInPages(string path)
    {
        await using var configured = factory.WithWebHostBuilder(b => b.WithGoogleAndApple());
        using var client = configured.CreateClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Continue with Google", html, StringComparison.Ordinal);
        Assert.Contains("Continue with Apple", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("Continue with Google", StringComparison.Ordinal) < html.IndexOf("Continue with Apple", StringComparison.Ordinal));
        Assert.Contains(
            "form-action 'self' https://accounts.google.com https://appleid.apple.com;",
            response.Headers.GetValues("Content-Security-Policy").Single(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyAFullyConfiguredProviderIsOffered()
    {
        // Apple without its key, Google complete.
        await using var partial = factory.WithWebHostBuilder(b => b
            .UseSetting("Authentication:Google:ClientId", "test-google-client")
            .UseSetting("Authentication:Google:ClientSecret", "test-google-secret")
            .UseSetting("Authentication:Apple:ClientId", "test.apple.services")
            .UseSetting("Authentication:Apple:TeamId", "TEAM123456")
            .UseSetting("Authentication:Apple:KeyId", "KEY1234567"));
        using var client = partial.CreateClient();
        var html = await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative));

        Assert.Contains("Continue with Google", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Continue with Apple", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Google", "https://accounts.google.com/", "client_id=test-google-client")]
    [InlineData("Apple", "https://appleid.apple.com/", "client_id=test.apple.services")]
    public async Task TheButtonSendsThePersonToTheProvider(string provider, string origin, string clientId)
    {
        await using var configured = factory.WithWebHostBuilder(b => b.WithGoogleAndApple());
        using var client = configured.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var token = ExternalSignInSettings.AntiforgeryToken(await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative)));

        using var response = await client.PostAsync(new Uri("/Account/PerformExternalLogin", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["provider"] = provider,
            ["ReturnUrl"] = "/",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith(origin, location, StringComparison.Ordinal);
        Assert.Contains(clientId, location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArrivingWithoutAProviderSignInExplainsInsteadOfFailing()
    {
        await using var configured = factory.WithWebHostBuilder(b => b.WithGoogleAndApple());
        using var client = configured.CreateClient();
        using var response = await client.GetAsync(new Uri("/Account/ExternalLogin?Action=LoginCallback", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign-in didn't finish", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProviderErrorIsNotEchoedBack()
    {
        await using var configured = factory.WithWebHostBuilder(b => b.WithGoogleAndApple());
        using var client = configured.CreateClient();
        var html = await client.GetStringAsync(new Uri("/Account/ExternalLogin?RemoteError=%3Cb%3Eevil%3C%2Fb%3E", UriKind.Relative));

        Assert.Contains("Sign-in didn't finish", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>evil", html, StringComparison.Ordinal);
        Assert.DoesNotContain("&lt;b&gt;evil", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Google", "true", "a@example.test")]
    [InlineData("Google", "True", "a@example.test")]
    [InlineData("Google", "false", null)]
    [InlineData("Google", null, null)]
    [InlineData("Apple", null, "a@example.test")]
    [InlineData("Other", "true", null)]
    public void OnlyProviderVerifiedAddressesAreTrusted(string provider, string? verified, string? expected)
    {
        var claims = new List<Claim> { new(ClaimTypes.Email, " a@example.test ") };
        if (verified is not null)
        {
            claims.Add(new Claim(ExternalSignIn.VerifiedEmailClaim, verified));
        }

        Assert.Equal(expected, ExternalSignIn.VerifiedEmail(provider, new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
    }
}

/// <summary>
/// The callback against a real database. A test-only endpoint stands in for the provider by
/// writing the external sign-in cookie the provider's handler would write.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class ExternalSignInAccountTests(PostgresFixture db) : IAsyncLifetime
{
    private WebApplicationFactory<Program> app = default!;

    public Task InitializeAsync()
    {
        app = new CouplesWebFactory().WithWebHostBuilder(b => b
            .WithGoogleAndApple()
            .UseSetting("ConnectionStrings:Couples", db.ConnectionString)
            .ConfigureServices(s => s.AddSingleton<IStartupFilter, FakeProviderCallback>()));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await app.DisposeAsync();

    [Fact]
    public async Task ANewPersonWithAVerifiedEmailGetsAConfirmedAccountAndIsSignedIn()
    {
        var email = NewEmail();
        using var client = Client();
        await SignInWithProvider(client, "Google", "g-" + email, email, verified: true);

        var page = await client.GetStringAsync(new Uri("/Account/ExternalLogin?Action=LoginCallback", UriKind.Relative));
        Assert.Contains("Create your account", page, StringComparison.Ordinal);
        Assert.Contains(email, page, StringComparison.Ordinal);

        using var created = await client.PostAsync(new Uri("/Account/ExternalLogin", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExternalSignInSettings.AntiforgeryToken(page),
            ["_handler"] = "external-register",
        }));

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Contains(created.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-bt-auth=", StringComparison.Ordinal));
        var user = await FindUser(email);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        Assert.Equal(["Google"], await Logins(user.Id));
    }

    [Fact]
    public async Task AnExistingConfirmedAccountWithTheSameEmailIsLinkedAndSignedIn()
    {
        var userId = await db.CreateUserAsync();
        var email = $"{userId:N}@example.test";
        using var client = Client();
        await SignInWithProvider(client, "Apple", "apple-" + userId, email.ToUpperInvariant(), verified: false);

        using var response = await client.GetAsync(new Uri("/Account/ExternalLogin?Action=LoginCallback", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-bt-auth=", StringComparison.Ordinal));
        Assert.Equal(["Apple"], await Logins(userId));
    }

    [Fact]
    public async Task AnUnconfirmedAccountIsNotLinked()
    {
        var email = NewEmail();
        var userId = await CreateUser(email, confirmed: false, twoFactor: false);
        using var client = Client();
        await SignInWithProvider(client, "Google", "g-" + email, email, verified: true);

        using var response = await client.GetAsync(new Uri("/Account/ExternalLogin?Action=LoginCallback", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("been confirmed yet", html, StringComparison.Ordinal);
        Assert.Empty(await Logins(userId));
    }

    [Fact]
    public async Task AnUnverifiedGoogleEmailIsNeverMatchedToAnAccount()
    {
        var userId = await db.CreateUserAsync();
        var email = $"{userId:N}@example.test";
        using var client = Client();
        await SignInWithProvider(client, "Google", "g-" + userId, email, verified: false);

        using var response = await client.GetAsync(new Uri("/Account/ExternalLogin?Action=LoginCallback", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("share a verified email", html, StringComparison.Ordinal);
        Assert.Empty(await Logins(userId));
    }

    [Fact]
    public async Task TwoFactorStillAppliesAfterLinking()
    {
        var email = NewEmail();
        var userId = await CreateUser(email, confirmed: true, twoFactor: true);
        using var client = Client();
        await SignInWithProvider(client, "Google", "g-" + email, email, verified: true);

        using var response = await client.GetAsync(new Uri("/Account/ExternalLogin?Action=LoginCallback", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Account/LoginWith2fa", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], c => c.StartsWith("__Host-bt-auth=", StringComparison.Ordinal));
        Assert.Equal(["Google"], await Logins(userId));
    }

    private HttpClient Client() => app.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.test";

    private static async Task SignInWithProvider(HttpClient client, string provider, string key, string email, bool verified)
    {
        var query = $"?provider={provider}&key={Uri.EscapeDataString(key)}&email={Uri.EscapeDataString(email)}&verified={verified}";
        using var response = await client.GetAsync(new Uri(FakeProviderCallback.Path + query, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> CreateUser(string email, bool confirmed, bool twoFactor)
    {
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = confirmed,
            TwoFactorEnabled = twoFactor,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<ApplicationUser> FindUser(string email)
    {
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CouplesDbContext>().Users.AsNoTracking()
            .SingleAsync(u => u.Email == email);
    }

    private async Task<List<string>> Logins(Guid userId)
    {
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CouplesDbContext>().UserLogins
            .Where(l => l.UserId == userId).Select(l => l.LoginProvider).ToListAsync();
    }

    /// <summary>Writes the external cookie as Google's or Apple's handler would after a successful sign-in.</summary>
    private sealed class FakeProviderCallback : IStartupFilter
    {
        public const string Path = "/test-only/provider-callback";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // Not app.Map: that would move the path base and so scope the cookie to this path.
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path != Path)
                {
                    await nextMiddleware(context);
                    return;
                }

                var q = context.Request.Query;
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, q["key"].ToString()),
                    new(ClaimTypes.Email, q["email"].ToString()),
                };
                if (q["verified"] == "True")
                {
                    claims.Add(new Claim(ExternalSignIn.VerifiedEmailClaim, "true"));
                }

                var properties = new AuthenticationProperties();
                properties.Items["LoginProvider"] = q["provider"].ToString();
                await context.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, q["provider"].ToString())), properties);
            });
            next(app);
        };
    }
}
