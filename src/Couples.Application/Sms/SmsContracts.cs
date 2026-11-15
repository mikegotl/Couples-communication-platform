using Couples.Domain.Common;

namespace Couples.Application.Sms;

/// <summary>
/// A user's own text-message settings (ADR-0020). Every method acts only on the calling user's row
/// and does nothing unless texts are available (<see cref="FeatureFlags.SmsMessaging"/> on and a
/// provider configured).
/// </summary>
public interface ISmsSettingsService
{
    Task<SmsSettingsView> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Sends a one-time code to <paramref name="phone"/>. Validation for a number that can't be read, RateLimited after three codes in an hour.</summary>
    Task<Result<SmsSettingsView>> StartAsync(Guid userId, string? phone, bool includeMessageText, bool agreedToTexts, CancellationToken cancellationToken = default);

    /// <summary>Checks the code, records consent and turns texts on. Conflict if another account already uses the number.</summary>
    Task<Result<SmsSettingsView>> ConfirmAsync(Guid userId, string? code, CancellationToken cancellationToken = default);

    Task<Result<SmsSettingsView>> SetIncludeMessageTextAsync(Guid userId, bool include, CancellationToken cancellationToken = default);

    /// <summary>Turns texts off and forgets the number. Waiting replies stay as private drafts.</summary>
    Task<Result<SmsSettingsView>> TurnOffAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <param name="Available">Texts can be used at all in this deployment.</param>
/// <param name="MaskedPhone">The number with all but the last 4 digits hidden, or null.</param>
/// <param name="State">Where the user is in setting texts up.</param>
/// <param name="HeldForSafety">The user's own safety settings currently stop texts both ways.</param>
public sealed record SmsSettingsView(bool Available, SmsSetupState State, string? MaskedPhone, bool IncludeMessageText, bool HeldForSafety, string? ServiceNumber);

public enum SmsSetupState
{
    Off = 0,
    AwaitingCode = 1,
    On = 2,

    /// <summary>The user replied STOP. Texting START, or setting up again, turns texts back on.</summary>
    Stopped = 3,
}

/// <summary>Turns a text a user sent us into the reply we text back (or none). Provider-neutral.</summary>
public interface ISmsInbound
{
    /// <returns>The reply text, or null to send nothing.</returns>
    Task<string?> HandleAsync(string from, string? body, CancellationToken cancellationToken = default);
}

/// <summary>Texts the in-app notifications that are due to people who turned texts on.</summary>
public interface ISmsDispatcher
{
    Task<int> DispatchDueAsync(CancellationToken cancellationToken = default);
}

/// <summary>The SMS provider. Implementations never log the number or the text.</summary>
public interface ISmsSender
{
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);
}

/// <param name="Accepted">The provider accepted the text for delivery.</param>
/// <param name="ProviderMessageId">The provider's id for the text, if any.</param>
/// <param name="RecipientOptedOut">The provider refused because the number replied STOP.</param>
public sealed record SmsSendResult(bool Accepted, string? ProviderMessageId = null, bool RecipientOptedOut = false);

public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Disabled, Fake (Development: an in-memory outbox at /dev/texts) or Twilio.</summary>
    public string Provider { get; set; } = "Disabled";

    /// <summary>The site's public https address, used for links in texts and to check Twilio's signature.</summary>
    public Uri? PublicBaseUrl { get; set; }

    /// <summary>Country calling code assumed for numbers typed without a leading + (digits only).</summary>
    public string DefaultCallingCode { get; set; } = "1";

    /// <summary>Most message texts one person gets in 24 hours. Others still arrive in the app.</summary>
    public int DailyTextsPerUser { get; set; } = 20;

    /// <summary>Longest message excerpt put in a text before it is cut with a link to the rest.</summary>
    public int MaxMessageCharacters { get; set; } = 480;

    public TwilioOptions Twilio { get; set; } = new();

    public bool IsConfigured => Provider switch
    {
        "Fake" => true,
        "Twilio" => !string.IsNullOrWhiteSpace(Twilio.AccountSid)
            && !string.IsNullOrWhiteSpace(Twilio.AuthToken)
            && (!string.IsNullOrWhiteSpace(Twilio.FromNumber) || !string.IsNullOrWhiteSpace(Twilio.MessagingServiceSid))
            && PublicBaseUrl is { Scheme: "https" },
        _ => false,
    };
}

public sealed class TwilioOptions
{
    public string? AccountSid { get; set; }

    /// <summary>Secret. From user-secrets or Key Vault, never committed.</summary>
    public string? AuthToken { get; set; }

    /// <summary>The number texts come from, in E.164 form. Either this or <see cref="MessagingServiceSid"/>.</summary>
    public string? FromNumber { get; set; }

    public string? MessagingServiceSid { get; set; }
}
