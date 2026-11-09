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
}
