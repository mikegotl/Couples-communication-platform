namespace Couples.Application;

/// <summary>
/// Server-side feature flag names. Flags are evaluated on the server only; the UI must
/// never be the sole gate for a flagged capability.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Phase 2 guided joint chat pilot. Default OFF in every environment. Enabling it
    /// requires independent clinical and security sign-off (see DECISIONS.md, ADR-0005).
    /// </summary>
    public const string Phase2JointChat = "Phase2JointChat";

    /// <summary>
    /// Signed-in users must agree to the current pilot terms and privacy notice before using
    /// the app. On in Production for the private pilot; off in Development.
    /// </summary>
    public const string PilotEnrollmentRequired = "PilotEnrollmentRequired";

    /// <summary>The in-app feedback form. On for the pilot.</summary>
    public const string FeedbackForm = "FeedbackForm";

    /// <summary>Every flag, for the admin overview.</summary>
    public static IReadOnlyList<string> All { get; } = [Phase2JointChat, PilotEnrollmentRequired, FeedbackForm];
}
