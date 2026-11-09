using System.Net;
using Couples.Application;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;

namespace Couples.IntegrationTests;

public sealed class SmokeTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    [Fact]
    public async Task HomePageLoadsAnonymously()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("BridgeTogether", html, StringComparison.Ordinal);
        Assert.Contains("not therapy", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LivenessEndpointIsHealthyWithoutDatabase()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadinessEndpointReportsUnavailableDatabase()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("/workspace")]
    [InlineData("/invite/some-token")]
    public async Task WorkspacePagesRequireSignIn(string path)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/Account/Register")]
    [InlineData("/Account/Login")]
    [InlineData("/Account/ForgotPassword")]
    public async Task AccountPagesLoadAnonymously(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Phase2JointChatIsOffByDefault()
    {
        using var scope = factory.Services.CreateScope();
        var features = scope.ServiceProvider.GetRequiredService<IFeatureManager>();

        Assert.False(await features.IsEnabledAsync(FeatureFlags.Phase2JointChat));
    }

    [Fact]
    public void MigrationsMatchTheWebAppModel()
    {
        // Guards against the app's Identity or entity configuration drifting from the
        // committed migrations, which would make Migrate() fail at startup.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
