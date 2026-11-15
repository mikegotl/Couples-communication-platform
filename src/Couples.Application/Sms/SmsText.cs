namespace Couples.Application.Sms;

/// <summary>
/// Every text the app sends, worded in one place. Notices are fixed per kind; the only user text
/// that ever goes into a text is a message the partner chose to share (when the recipient asked for
/// it) or the user's own reply echoed back for SEND. Nothing about safety answers, safety mode,
/// private notes, drafts the user didn't text us, intake or exercises is ever texted.
/// </summary>
public static class SmsText
{
    public const string Brand = "BridgeTogether";

    /// <summary>Words a user can reply to confirm the reply waiting to be shared.</summary>
    public static readonly IReadOnlySet<string> SendWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SEND" };

    /// <summary>Carrier-standard opt-out words. The provider also acts on these.</summary>
    public static readonly IReadOnlySet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT", "OPTOUT", "REVOKE" };

    public static readonly IReadOnlySet<string> StartWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "START", "UNSTOP" };

    public static readonly IReadOnlySet<string> HelpWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HELP", "INFO" };

    public static string Code(string code) =>
        $"{Brand} code: {code}. It expires in 10 minutes. If you didn't ask for it, ignore this text.";

    public static string Notice(bool isReply, string messagesUrl) =>
        (isReply ? $"{Brand}: Your partner replied to a message you shared." : $"{Brand}: Your partner shared a message with you.")
        + $" Read it at {messagesUrl} or reply here to answer. Reply STOP to stop texts.";

    public static string WithMessage(bool isReply, string body, int maxCharacters, string messagesUrl)
    {
        var excerpt = body.Length <= maxCharacters
            ? body
            : body[..maxCharacters].TrimEnd() + $"… (read the rest at {messagesUrl})";
        var heading = isReply ? $"{Brand}: Your partner replied:" : $"{Brand}: Your partner wrote:";
        return $"{heading}\n\n{excerpt}\n\nReply here to answer; you'll see it before it's sent. Reply STOP to stop texts.";
    }

    public static string ConfirmReply(string body) =>
        $"{Brand}: Your partner will see:\n\n{body}\n\nReply SEND within 30 minutes to share it. Otherwise it stays a private draft in the app, where you can edit it or ask the coach.";

    public const string Sent = Brand + ": Shared. Your partner can read it now.";

    public const string NothingWaiting = Brand + ": There's no reply waiting to send. Text your reply first, then SEND.";

    public const string DraftChanged = Brand + ": That reply changed in the app, so it wasn't shared. Look it over there before sending.";

    public const string TooLong = Brand + ": That's longer than a message can be. Try a shorter reply, or write it in the app.";

    public const string NoPartner = Brand + ": There's nobody in your shared space to send this to yet. Your words are saved as a private draft in the app.";

    public const string Slow = Brand + ": You've sent a lot this hour, so this one waits as a private draft in the app. Taking a breather can help.";

    public const string Unavailable = Brand + ": Please open the app to continue.";

    public const string NotSetUp = Brand + ": Texting isn't set up for this number. You can turn it on in the app.";

    public const string Restarted = Brand + ": Texts are back on. Reply STOP to stop.";

    public static string Help(string settingsUrl) =>
        $"{Brand}: Texts about messages from your partner. Reply to answer, SEND to confirm, STOP to stop. Manage texts at {settingsUrl}. Msg & data rates may apply.";

    public static string PartnerPaused(DateTimeOffset until) =>
        $"{Brand}: Your partner has paused new messages until {until:d MMM HH:mm} UTC. Your reply is saved as a private draft in the app.";

    public const string Failed = Brand + ": Something went wrong, so nothing was shared. Your reply is kept as a private draft in the app.";
}
