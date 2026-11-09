using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Intake;

/// <summary>
/// One topic a user chose during intake, with their private notes about it. Owner only:
/// a partner never sees which topics were chosen or what was written.
/// </summary>
[Privacy(DataClassification.PrivateSensitive, "Kept until the owner removes it, starts intake over, or deletes their account.")]
public sealed class ConcernSelection : Entity
{
    public const int MaxCustomLabelLength = 60;
    public const int MaxSummaryLength = 4_000;

    private ConcernSelection()
    {
    }

    public Guid IntakeProfileId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string CategoryKey { get; private set; } = string.Empty;

    /// <summary>The user's own name for a custom topic; null for catalog topics.</summary>
    public string? CustomLabel { get; private set; }

    /// <summary>The user's private notes on what this topic means for them.</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>When the user acknowledged the category's content warning, if it has one.</summary>
    public DateTimeOffset? WarningAcknowledgedAt { get; private set; }

    internal static ConcernSelection Create(IntakeProfile profile, ConcernCategory category, string? customLabel, DateTimeOffset? acknowledgedAt, DateTimeOffset now)
    {
        var selection = new ConcernSelection
        {
            IntakeProfileId = profile.Id,
            OwnerUserId = profile.OwnerUserId,
            CategoryKey = category.Key,
            CustomLabel = customLabel,
            WarningAcknowledgedAt = acknowledgedAt,
        };
        selection.Touch(now);
        return selection;
    }

    internal Result UpdateSummary(string? summary, DateTimeOffset now)
    {
        var clean = summary?.Trim() ?? string.Empty;
        if (clean.Length > MaxSummaryLength)
        {
            return Result.Fail(DomainError.Validation);
        }

        Summary = clean;
        Touch(now);
        return Result.Ok();
    }
}
