namespace Couples.IntegrationTests.Mobile;

/// <summary>
/// The brand fonts moved to the shared Couples.UI library. The test host can't serve library static
/// assets (MudBlazor's fail the same way), so this checks the link (the
/// published app includes the files under wwwroot/_content/Couples.UI).
/// </summary>
public sealed class SharedUiAssetsTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    [Fact]
    public async Task PagesLinkTheSharedBrandStylesheet()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("_content/Couples.UI/brand", html, StringComparison.Ordinal);
    }
}
