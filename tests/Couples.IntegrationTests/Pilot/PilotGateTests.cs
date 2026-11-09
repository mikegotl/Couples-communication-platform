using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Couples.Application.Pilot;
using Couples.Domain.Common;
using Couples.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

namespace Couples.IntegrationTests.Pilot;

/// <summary>The server-side pilot gate, tested in a minimal pipeline with a signed-in user.</summary>
public sealed class PilotGateTests
{
    [Theory]
    [InlineData("/workspace", true, false, HttpStatusCode.Redirect)]
    [InlineData("/messages/new?replyTo=x", true, false, HttpStatusCode.Redirect)]
    [InlineData("/workspace", true, true, HttpStatusCode.OK)]
    [InlineData("/workspace", false, false, HttpStatusCode.OK)]
    [InlineData("/pilot", true, false, HttpStatusCode.OK)]
    [InlineData("/Account/Manage", true, false, HttpStatusCode.OK)]
    [InlineData("/terms", true, false, HttpStatusCode.OK)]
    [InlineData("/privacy-notice", true, false, HttpStatusCode.OK)]
    [InlineData("/support", true, false, HttpStatusCode.OK)]
    [InlineData("/app.css", true, false, HttpStatusCode.OK)]
    public async Task PagesNeedEnrollmentWhileTheFlagIsOn(string path, bool flagOn, bool enrolled, HttpStatusCode expected)
    {
        using var host = await StartAsync(flagOn, enrolled, signedIn: true);
        using var response = await host.GetTestClient().GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Redirect)
        {
            Assert.Equal("/pilot?returnUrl=" + Uri.EscapeDataString(path), response.Headers.Location?.OriginalString);
        }
    }

    [Fact]
    public async Task TheInteractiveConnectionIsRefusedWithoutEnrollment()
    {
        using var host = await StartAsync(flagOn: true, enrolled: false, signedIn: true);
        using var content = new StringContent(string.Empty);
        using var response = await host.GetTestClient().PostAsync(new Uri("/_blazor/negotiate?negotiateVersion=1", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SignedOutVisitorsAreLeftToNormalAuthorization()
    {
        using var host = await StartAsync(flagOn: true, enrolled: false, signedIn: false);
        using var response = await host.GetTestClient().GetAsync(new Uri("/workspace", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<IHost> StartAsync(bool flagOn, bool enrolled, bool signedIn)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IFeatureManager>(new FixedFeatures(flagOn));
                    services.AddScoped<IPilotService>(_ => new FixedPilot(enrolled));
                    services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuth>("test", o => o.ClaimsIssuer = signedIn ? "in" : "out");
                })
                .Configure(app =>
                {
                    app.UseAuthentication();
                    app.UsePilotGate();
                    app.Run(context => context.Response.WriteAsync("ok"));
                }))
            .Build();
        await host.StartAsync();
        return host;
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.ClaimsIssuer != "in")
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
        }
    }

    private sealed class FixedFeatures(bool on) : IFeatureManager
    {
        public IAsyncEnumerable<string> GetFeatureNamesAsync() => AsyncEnumerable.Empty<string>();

        public Task<bool> IsEnabledAsync(string feature) => Task.FromResult(on);

        public Task<bool> IsEnabledAsync<TContext>(string feature, TContext context) => Task.FromResult(on);
    }

    private sealed class FixedPilot(bool enrolled) : IPilotService
    {
        public Task<bool> IsEnrolledAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(enrolled);

        public Task<PilotStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<PilotStatus>> EnrollAsync(Guid userId, string termsVersion, string privacyNoticeVersion, string? accessCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PilotStatus> WithdrawAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
