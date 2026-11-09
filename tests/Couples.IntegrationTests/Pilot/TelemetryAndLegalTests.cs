using System.Diagnostics;
using System.Net;
using Couples.Domain.Pilot;
using Couples.Infrastructure.Observability;
using Couples.Web.Legal;

namespace Couples.IntegrationTests.Pilot;

/// <summary>Milestone 11: telemetry carries no private data; the legal texts are the reviewed versions.</summary>
public sealed class TelemetryAndLegalTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    [Fact]
    public void SpansLoseUrlsAddressesAndAgents()
    {
        using var activity = new Activity("GET /invite/s3cret-token-value").Start();
        activity.SetTag("http.request.method", "GET");
        activity.SetTag("http.route", "/invite/{Token}");
        activity.SetTag("url.path", "/invite/s3cret-token-value");
        activity.SetTag("url.query", "?returnUrl=%2Fmessages");
        activity.SetTag("client.address", "203.0.113.9");
        activity.SetTag("user_agent.original", "Mozilla/5.0");
        activity.SetTag("http.response.status_code", 200);

        new TelemetryScrubbing.ScrubbingActivityProcessor().OnEnd(activity);

        Assert.Equal("GET /invite/{Token}", activity.DisplayName);
        Assert.Null(activity.GetTagItem("url.path"));
        Assert.Null(activity.GetTagItem("url.query"));
        Assert.Null(activity.GetTagItem("client.address"));
        Assert.Null(activity.GetTagItem("user_agent.original"));
        Assert.Equal(200, activity.GetTagItem("http.response.status_code"));
        Assert.DoesNotContain("s3cret", string.Join(" ", activity.Tags.Select(t => t.Value)), StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionMessagesAreDroppedButTypeAndStackKept()
    {
        Exception original;
        try
        {
            throw new InvalidOperationException("Body was: I feel so alone with this");
        }
        catch (InvalidOperationException e)
        {
            original = e;
        }

        var redacted = new TelemetryScrubbing.RedactedException(original);

        Assert.DoesNotContain("alone", redacted.Message, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", redacted.Message, StringComparison.Ordinal);
        Assert.Equal(original.StackTrace, redacted.StackTrace);
    }

    [Theory]
    [InlineData("Login failed for jo.bloggs@example.com", "Login failed for [email]")]
    [InlineData("Request GET /invite/AbC123-xyz?x=1 failed", "Request GET /invite/[token]?x=1 failed")]
    [InlineData("Nothing to hide here", "Nothing to hide here")]
    public void LogTextMasksEmailsAndInviteTokens(string input, string expected) =>
        Assert.Equal(expected, TelemetryScrubbing.Mask(input));

    [Fact]
    public void LegalTextsAreTheVersionsParticipantsAgreeTo()
    {
        Assert.Contains("Version: " + PilotDocuments.TermsVersion, LegalDocuments.Markdown(LegalDocuments.PilotTerms), StringComparison.Ordinal);
        Assert.Contains("Version: " + PilotDocuments.PrivacyNoticeVersion, LegalDocuments.Markdown(LegalDocuments.PrivacyNotice), StringComparison.Ordinal);

        // The embedded copy is the reviewed file in docs/legal.
        Assert.Equal(File.ReadAllText(FindRepoFile("docs/legal/pilot-terms.md")), LegalDocuments.Markdown(LegalDocuments.PilotTerms));
        Assert.Equal(File.ReadAllText(FindRepoFile("docs/legal/privacy-notice.md")), LegalDocuments.Markdown(LegalDocuments.PrivacyNotice));
    }

    [Fact]
    public void LegalTextsDoNotClaimTherapyOrEmergencyHelp()
    {
        var terms = LegalDocuments.Markdown(LegalDocuments.PilotTerms);
        Assert.Contains("It is not therapy", terms.Replace("**", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("(?i)we will (call|contact) (emergency|the police|someone)", terms + LegalDocuments.Markdown(LegalDocuments.PrivacyNotice));
    }

    [Theory]
    [InlineData("/terms", "Private pilot terms")]
    [InlineData("/privacy-notice", "Privacy notice (private pilot)")]
    public async Task LegalPagesArePublic(string path, string heading)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<h1>{heading}</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Draft, pending legal review", html, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(relative);
    }
}
