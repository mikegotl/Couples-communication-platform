using Couples.Application.Safety;
using Couples.Application.Sms;
using Couples.Domain.Common;
using Couples.Domain.Sms;

namespace Couples.UnitTests.Sms;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+44 7700 900123", "+447700900123")]
    [InlineData("(555) 123-4567", "+15551234567")]
    [InlineData("555.123.4567", "+15551234567")]
    [InlineData("0044 7700 900123", "+447700900123")]
    [InlineData("+1 (555) 123-4567", "+15551234567")]
    public void ReadsCommonFormats(string typed, string expected)
    {
        Assert.True(PhoneNumber.TryNormalize(typed, "1", out var e164));
        Assert.Equal(expected, e164);
    }

    [Fact]
    public void DropsTheTrunkZeroForNationalNumbers()
    {
        Assert.True(PhoneNumber.TryNormalize("07700 900123", "44", out var e164));
        Assert.Equal("+447700900123", e164);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("call me")]
    [InlineData("+12")]
    [InlineData("+1234567890123456")]
    [InlineData("555-1234; DROP")]
    public void RejectsWhatIsNotANumber(string? typed) => Assert.False(PhoneNumber.TryNormalize(typed, "1", out _));

    [Fact]
    public void MasksAllButTheLastFourDigits() => Assert.Equal("•••• 4567", PhoneNumber.Mask("+15551234567"));
}

public sealed class SmsSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static SmsSettings WithCode(string code = "123456")
    {
        var s = SmsSettings.Start(Guid.NewGuid(), Now);
        Assert.True(s.IssueCode("+15551234567", code, includeMessageText: false, Now).Succeeded);
        return s;
    }

    [Fact]
    public void RightCodeVerifiesAndStoresNoCode()
    {
        var s = WithCode();
        Assert.True(s.Confirm(" 123456 ", Now.AddMinutes(1)).Succeeded);
        Assert.True(s.CanReceive);
        Assert.Null(s.CodeHash);
    }

    [Fact]
    public void CodeIsStoredOnlyAsAHash()
    {
        var s = WithCode("654321");
        Assert.DoesNotContain("654321", s.CodeHash!, StringComparison.Ordinal);
    }

    [Fact]
    public void FiveWrongTriesUseUpTheCode()
    {
        var s = WithCode();
        for (var i = 0; i < SmsSettings.MaxCodeAttempts; i++)
        {
            Assert.Equal(DomainError.Validation, s.Confirm("000000", Now).Error);
        }

        Assert.Equal(DomainError.InvitationInvalid, s.Confirm("123456", Now).Error);
        Assert.False(s.IsVerified);
    }

    [Fact]
    public void CodeExpiresAfterTenMinutes() =>
        Assert.Equal(DomainError.InvitationInvalid, WithCode().Confirm("123456", Now + SmsSettings.CodeLifetime).Error);

    [Fact]
    public void AtMostThreeCodesAnHour()
    {
        var s = WithCode();
        Assert.True(s.IssueCode("+15551234567", "111111", false, Now.AddMinutes(1)).Succeeded);
        Assert.True(s.IssueCode("+15551234567", "222222", false, Now.AddMinutes(2)).Succeeded);
        Assert.Equal(DomainError.RateLimited, s.IssueCode("+15551234567", "333333", false, Now.AddMinutes(3)).Error);
        Assert.True(s.IssueCode("+15551234567", "444444", false, Now.AddHours(1)).Succeeded);
    }

    [Fact]
    public void ANewNumberStartsUnverifiedAndDropsAWaitingReply()
    {
        var s = WithCode();
        Assert.True(s.Confirm("123456", Now).Succeeded);
        s.AwaitConfirmation(Guid.NewGuid(), "fp", Now);
        Assert.True(s.IssueCode("+447700900123", "999999", false, Now.AddMinutes(1)).Succeeded);
        Assert.False(s.IsVerified);
        Assert.Null(s.PendingConfirmation(Now.AddMinutes(1)));
    }

    [Fact]
    public void StopEndsTextsAndStartOnlyWorksWhenVerified()
    {
        var unverified = WithCode();
        Assert.False(unverified.OptIn(Now));

        var s = WithCode();
        Assert.True(s.Confirm("123456", Now).Succeeded);
        s.AwaitConfirmation(Guid.NewGuid(), "fp", Now);
        s.OptOut(Now);
        Assert.False(s.CanReceive);
        Assert.Null(s.PendingConfirmation(Now));
        Assert.True(s.OptIn(Now));
        Assert.True(s.CanReceive);
    }

    [Fact]
    public void AWaitingReplyExpiresAfterThirtyMinutes()
    {
        var s = WithCode();
        var draft = Guid.NewGuid();
        s.AwaitConfirmation(draft, "fp", Now);
        Assert.Equal(draft, s.PendingConfirmation(Now.AddMinutes(29))!.Value.DraftId);
        Assert.Null(s.PendingConfirmation(Now + SmsSettings.ConfirmWindow));
    }
}

public sealed class SmsTextTests
{
    [Fact]
    public void NoticesCarryNoMessageWords()
    {
        var notice = SmsText.Notice(false, "https://example.test/messages");
        Assert.Equal("BridgeTogether: Your partner shared a message with you. Read it at https://example.test/messages or reply here to answer. Reply STOP to stop texts.", notice);
    }

    [Fact]
    public void LongMessagesAreCutWithALink()
    {
        var text = SmsText.WithMessage(false, new string('a', 1000), 100, "https://example.test/messages");
        Assert.Contains(new string('a', 100) + "… (read the rest at https://example.test/messages)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 101), text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoFixedTextMentionsSafety()
    {
        string[] all =
        [
            SmsText.Code("123456"), SmsText.Notice(true, "u"), SmsText.Notice(false, "u"), SmsText.WithMessage(true, "x", 10, "u"),
            SmsText.ConfirmReply("x"), SmsText.Sent, SmsText.NothingWaiting, SmsText.DraftChanged, SmsText.TooLong, SmsText.NoPartner,
            SmsText.Slow, SmsText.Unavailable, SmsText.NotSetUp, SmsText.Restarted, SmsText.Help("u"), SmsText.PartnerPaused(DateTimeOffset.UnixEpoch), SmsText.Failed,
        ];
        string[] banned = ["safety", "safe", "unsafe", "danger", "abuse", "hold", "held", "check-in", "check in"];
        foreach (var text in all)
        {
            foreach (var word in banned)
            {
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var line in SafetyCopy.All)
            {
                Assert.DoesNotContain(line, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void SendIsNotACarrierKeyword()
    {
        foreach (var word in SmsText.SendWords)
        {
            Assert.DoesNotContain(word, SmsText.StopWords);
            Assert.DoesNotContain(word, SmsText.StartWords);
            Assert.DoesNotContain(word, SmsText.HelpWords);
            Assert.NotEqual("YES", word, StringComparer.OrdinalIgnoreCase);
        }
    }
}
