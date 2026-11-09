using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Couples.IntegrationTests.JointChat;

/// <summary>Milestone 12 gate: with the flag off the joint chat pages don't exist for anyone.</summary>
public sealed class JointChatHttpTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    [Theory]
    [InlineData("/joint-chat")]
    [InlineData("/admin/joint-chat")]
    [InlineData("/JOINT-CHAT/")]
    public async Task PagesAre404WhileTheFlagIsOff(string path)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var get = await client.GetAsync(new Uri(path, UriKind.Relative));
        using var post = await client.PostAsync(new Uri(path, UriKind.Relative), new StringContent(""));

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    }

    [Fact]
    public async Task WithTheFlagOnThePageExistsButNeedsSignIn()
    {
        await using var on = factory.WithWebHostBuilder(b => b.UseSetting("FeatureManagement:Phase2JointChat", "true"));
        using var client = on.CreateClient(new() { AllowAutoRedirect = false });
        using var response = await client.GetAsync(new Uri("/joint-chat", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }
}
