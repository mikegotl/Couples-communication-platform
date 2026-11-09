namespace Couples.Domain.Privacy;

/// <summary>How sensitive the data held by an entity is. Drives access, logging and retention.</summary>
public enum DataClassification
{
    /// <summary>Operational metadata with no personal content (ids, timestamps, states).</summary>
    Internal = 1,

    /// <summary>Identifies or describes a person (account, membership, consent decisions).</summary>
    Personal = 2,

    /// <summary>
    /// Sensitive content visible only to the one user who wrote it (reflections, drafts). Never
    /// visible to a partner, never logged, never in notifications or analytics.
    /// </summary>
    PrivateSensitive = 3,

    /// <summary>
    /// Sensitive content that its author explicitly approved for one recipient. Visible only to the
    /// sender and that recipient while both remain members; never logged or in notifications.
    /// </summary>
    SharedSensitive = 4,
}

/// <summary>
/// Declares the classification and retention rule of a persisted type. Required on every entity;
/// a test fails the build if one is missing. Keep docs/privacy/data-inventory.md in step.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PrivacyAttribute(DataClassification classification, string retention) : Attribute
{
    public DataClassification Classification { get; } = classification;

    public string Retention { get; } = retention;
}
