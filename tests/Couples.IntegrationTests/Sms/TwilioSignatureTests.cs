using Couples.Infrastructure.Sms;

namespace Couples.IntegrationTests.Sms;

/// <summary>Twilio webhook signatures (no database).</summary>
public sealed class TwilioSignatureTests
{
    private static readonly KeyValuePair<string, string>[] Form =
    [
        new("CallSid", "CA1234567890ABCDE"),
        new("Caller", "+12349013030"),
        new("Digits", "1234"),
        new("From", "+12349013030"),
        new("To", "+18005551212"),
    ];

    [Fact]
    public void MatchesTwilioPublishedExample() =>
        Assert.True(TwilioSignature.IsValid("12345", "https://mycompany.com/myapp.php?foo=1&bar=2", Form.Reverse(), "0/KCTR6DLpKmkAf8muzZqo1nDgQ="));

    [Fact]
    public void RejectsTamperedOrMissingSignatures()
    {
        var url = "https://example.test/sms/twilio";
        var good = TwilioSignature.Compute("token", url, Form);
        Assert.True(TwilioSignature.IsValid("token", url, Form, good));
        Assert.False(TwilioSignature.IsValid("token", url, [.. Form, new("Body", "extra")], good));
        Assert.False(TwilioSignature.IsValid("other", url, Form, good));
        Assert.False(TwilioSignature.IsValid("token", url + "x", Form, good));
        Assert.False(TwilioSignature.IsValid("token", url, Form, null));
        Assert.False(TwilioSignature.IsValid(null, url, Form, good));
    }
}
