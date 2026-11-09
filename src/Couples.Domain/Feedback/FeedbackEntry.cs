using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Feedback;

public enum FeedbackArea
{
    General,
    Messaging,
    Exercises,
    Coach,
    Safety,
    Accessibility,
    Other,
}

/// <summary>
/// Pilot feedback. Anonymous unless the participant chooses to let the team contact them, in
/// which case (and only then) it is linked to their account. The form asks people not to write
/// private details, but the text is still treated as personal data.
/// </summary>
[Privacy(DataClassification.Personal, "Purged after 365 days; linked entries are deleted with the account.")]
public sealed class FeedbackEntry : Entity
{
    public const int MaxTextLength = 2_000;

    private FeedbackEntry()
    {
    }

    public FeedbackArea Area { get; private set; }

    /// <summary>1 (poor) to 5 (great), or none.</summary>
    public int? Rating { get; private set; }

    public string? Text { get; private set; }

    /// <summary>Set only when the participant agreed to be contacted about this feedback.</summary>
    public Guid? ContactUserId { get; private set; }

    public static Result<FeedbackEntry> Create(FeedbackArea area, int? rating, string? text, Guid? contactUserId, DateTimeOffset now)
    {
        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (!Enum.IsDefined(area) || rating is < 1 or > 5 || (rating is null && trimmed is null) || trimmed?.Length > MaxTextLength)
        {
            return DomainError.Validation;
        }

        var entry = new FeedbackEntry { Area = area, Rating = rating, Text = trimmed, ContactUserId = contactUserId };
        entry.Touch(now);
        return Result.Ok(entry);
    }
}
