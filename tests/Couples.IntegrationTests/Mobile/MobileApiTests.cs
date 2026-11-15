using System.Net;
using System.Net.Http.Json;
using Couples.Application.Mobile;

namespace Couples.IntegrationTests.Mobile;

public sealed class MobileApiTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    [Fact]
    public async Task AppStatusIsServedAnonymouslyAsJson()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/app-status", UriKind.Relative));
        var status = await response.Content.ReadFromJsonAsync<AppStatus>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new AppStatus("BridgeTogether", AppStatus.CurrentApiVersion, "0.1.0"), status);
    }

    [Fact]
    public async Task UnknownApiPathIsNotFound()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
