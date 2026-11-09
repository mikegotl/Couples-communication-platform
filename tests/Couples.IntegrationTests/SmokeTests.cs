using System.Net;
using Couples.Application;
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

    [Fact]
    public async Task Phase2JointChatIsOffByDefault()
    {
        using var scope = factory.Services.CreateScope();
        var features = scope.ServiceProvider.GetRequiredService<IFeatureManager>();

        Assert.False(await features.IsEnabledAsync(FeatureFlags.Phase2JointChat));
    }
}
