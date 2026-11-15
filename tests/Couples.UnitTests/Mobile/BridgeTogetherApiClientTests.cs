using System.Net;
using System.Text;
using Couples.Application.Mobile;
using Couples.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Couples.UnitTests.Mobile;

public sealed class BridgeTogetherApiClientTests
{
    [Fact]
    public async Task ReadsStatusFromServer()
    {
        using var http = Client(_ => Json("""{"service":"BridgeTogether","apiVersion":1,"minimumAppVersion":"0.2.0"}"""));

        var result = await new BridgeTogetherApiClient(http).GetStatusAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(new AppStatus("BridgeTogether", 1, "0.2.0"), result.Value);
    }

    [Fact]
    public async Task CallsTheVersionedPathUnderTheBaseAddress()
    {
        Uri? requested = null;
        using var http = Client(request =>
        {
            requested = request.RequestUri;
            return Json("""{"service":"BridgeTogether","apiVersion":1,"minimumAppVersion":"0.1.0"}""");
        }, new Uri("https://example.test/prefix/"));

        await new BridgeTogetherApiClient(http).GetStatusAsync();

        Assert.Equal(new Uri("https://example.test/prefix/api/v1/app-status"), requested);
    }

    [Fact]
    public async Task ServerErrorIsAFailureNotAnException()
    {
        using var http = Client(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await new BridgeTogetherApiClient(http).GetStatusAsync();

        Assert.Equal(ApiFailure.ServerError, result.Failure);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task NetworkErrorIsUnreachable()
    {
        using var http = Client(_ => throw new HttpRequestException("offline"));

        var result = await new BridgeTogetherApiClient(http).GetStatusAsync();

        Assert.Equal(ApiFailure.Unreachable, result.Failure);
    }

    [Theory]
    [InlineData("<html>Sign in to the hotel Wi-Fi</html>")]
    [InlineData("""{"service":"SomethingElse","apiVersion":1,"minimumAppVersion":"0.1.0"}""")]
    public async Task AnswerFromAnotherServerIsRejected(string body)
    {
        using var http = Client(_ => Json(body));

        var result = await new BridgeTogetherApiClient(http).GetStatusAsync();

        Assert.Equal(ApiFailure.UnexpectedServer, result.Failure);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0", true)]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("0.1.0", "not a version", true)]
    public void SupportsAppVersionsFromTheMinimumUp(string app, string minimum, bool expected)
    {
        var status = new AppStatus("BridgeTogether", AppStatus.CurrentApiVersion, minimum);

        Assert.Equal(expected, status.Supports(Version.Parse(app)));
    }

    [Fact]
    public void RefusesPlainHttpUnlessAllowed()
    {
        using var provider = new ServiceCollection()
            .AddBridgeTogetherApiClient(o => o.BaseAddress = new Uri("http://example.test/"))
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<BridgeTogetherApiClient>());
    }

    [Fact]
    public void AddsTrailingSlashToBaseAddress()
    {
        using var provider = new ServiceCollection()
            .AddBridgeTogetherApiClient(o => o.BaseAddress = new Uri("https://example.test/app"))
            .BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(nameof(BridgeTogetherApiClient));

        Assert.Equal(new Uri("https://example.test/app/"), http.BaseAddress);
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond, Uri? baseAddress = null) =>
        new(new StubHandler(respond)) { BaseAddress = baseAddress ?? new Uri("https://example.test/") };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
