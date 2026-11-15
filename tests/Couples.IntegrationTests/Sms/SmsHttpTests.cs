using System.Net;
using Couples.Infrastructure.Sms;
using Microsoft.AspNetCore.Hosting;

namespace Couples.IntegrationTests.Sms;

/// <summary>The Twilio webhook: hidden while texts are off, and refuses anything Twilio didn't sign.</summary>
public sealed class SmsHttpTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    private static readonly KeyValuePair<string, string>[] Form = [new("From", "+15555550101"), new("Body", "hello")];

    [Fact]
    public async Task WebhookIs404WhileTextsAreOff()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsync(new Uri("/sms/twilio", UriKind.Relative), new FormUrlEncodedContent(Form));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnsignedOrWronglySignedRequestsAreRefused()
    {
        await using var on = factory.WithWebHostBuilder(b => b
            .UseSetting("FeatureManagement:SmsMessaging", "true")
            .UseSetting("Sms:Provider", "Twilio")
            .UseSetting("Sms:PublicBaseUrl", "https://app.example.test")
            .UseSetting("Sms:Twilio:AccountSid", "AC00000000000000000000000000000000")
            .UseSetting("Sms:Twilio:AuthToken", "test-token")
            .UseSetting("Sms:Twilio:FromNumber", "+15555550100"));
        using var client = on.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        using var unsigned = await client.PostAsync(new Uri("/sms/twilio", UriKind.Relative), new FormUrlEncodedContent(Form));
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);

        using var wrong = new HttpRequestMessage(HttpMethod.Post, new Uri("/sms/twilio", UriKind.Relative)) { Content = new FormUrlEncodedContent(Form) };
        wrong.Headers.Add("X-Twilio-Signature", TwilioSignature.Compute("not-the-token", "https://app.example.test/sms/twilio", Form));
        using var refused = await client.SendAsync(wrong);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }
}
