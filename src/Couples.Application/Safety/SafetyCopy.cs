namespace Couples.Application.Safety;

/// <summary>
/// User-facing safety wording, kept in one place for clinician and legal review (see
/// docs/safety/clinical-review.md). PLACEHOLDER pending review. It must never claim the app
/// will contact anyone, monitor anyone or respond to an emergency.
/// </summary>
public static class SafetyCopy
{
    public const string ImmediateDanger =
        "If you or anyone else is in immediate danger, call your local emergency number now. This app can't contact anyone for you and doesn't monitor what you write.";

    public const string NotACrisisService =
        "BridgeTogether is a communication tool. It isn't a crisis, counselling or emergency service.";

    public const string NoPressure =
        "You don't have to work on this relationship here, or at all. Working on it together isn't right for every couple, especially if you don't feel safe.";

    public const string SelfHarm =
        "If you're thinking about hurting yourself or ending your life, please reach out now to a crisis line or someone you trust. You don't have to go through this alone.";

    public const string UnsafeResult =
        "Thank you for answering. Your answers suggest you may not feel safe with your partner. Shared features are turned off for you while that's the case, and your partner isn't told anything.";

    public const string ConcernResult =
        "Thank you for answering. Some of your answers suggest things may be difficult. Support services can help you think it through, privately.";

    public const string NoConcernsResult =
        "Thank you for answering. You can come back to this check at any time; things can change.";

    public const string SharingHeld =
        "Sharing with your partner is off while safety mode is on or your safety check suggests you may not be safe. Your drafts stay private to you.";

    public const string SafetyModeExplained =
        "Safety mode turns off sharing with your partner, stops notices from this app, and hides your partner's messages unless you choose to look. Your partner isn't told. Their messages still arrive, so nothing changes on their side.";

    public const string QuickExit =
        "\"Leave quickly\" opens a neutral website in this tab. It can't erase your browser history; if someone may check your device, consider a private browsing window and logging out.";

    /// <summary>Every string above, for review tooling and tests.</summary>
    public static IReadOnlyList<string> All { get; } =
        [ImmediateDanger, NotACrisisService, NoPressure, SelfHarm, UnsafeResult, ConcernResult, NoConcernsResult, SharingHeld, SafetyModeExplained, QuickExit];
}
