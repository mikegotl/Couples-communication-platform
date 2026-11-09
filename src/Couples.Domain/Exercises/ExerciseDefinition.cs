using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Exercises;

public enum ClinicianReviewStatus
{
    PendingClinicianReview,
    ChangesRequested,
    ClinicianApproved,
    Retired,
}

/// <summary>
/// A published version of an exercise, with its clinician review metadata. Content comes from
/// <see cref="ExerciseCatalog"/>; review status is recorded by administrators and never by code.
/// </summary>
[Privacy(DataClassification.Internal, "Kept while any attempt uses it; content is product material, not user data.")]
public sealed class ExerciseDefinition : Entity
{
    public const int MaxKeyLength = 60;
    public const int MaxReviewerLength = 120;
    public const int MaxReviewNotesLength = 2_000;

    private static readonly JsonSerializerOptions HashOptions = new() { WriteIndented = false };

    private ExerciseDefinition()
    {
    }

    public string Key { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public int EstimatedMinutes { get; private set; }

    public IReadOnlyList<ExerciseStep> Steps { get; private set; } = [];

    /// <summary>SHA-256 of the authored content, to detect edits made without a new version.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    public ClinicianReviewStatus ReviewStatus { get; private set; }

    public string? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? ReviewNotes { get; private set; }

    public static ExerciseDefinition Publish(ExerciseTemplate template, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(template);
        var definition = new ExerciseDefinition
        {
            Key = template.Key,
            Version = template.Version,
            Title = template.Title,
            Summary = template.Summary,
            EstimatedMinutes = template.EstimatedMinutes,
            Steps = template.Steps,
            ContentHash = HashOf(template),
            ReviewStatus = ClinicianReviewStatus.PendingClinicianReview,
        };
        definition.Touch(now);
        return definition;
    }

    public static string HashOf(ExerciseTemplate template) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(template, HashOptions))));

    public ExerciseStep? FindStep(string stepKey) => Steps.FirstOrDefault(s => s.Key == stepKey);

    public Result RecordReview(ClinicianReviewStatus status, string? reviewer, string? notes, DateTimeOffset now)
    {
        var name = reviewer?.Trim();
        var cleanNotes = notes?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxReviewerLength || (cleanNotes?.Length ?? 0) > MaxReviewNotesLength)
        {
            return Result.Fail(DomainError.Validation);
        }

        ReviewStatus = status;
        ReviewedBy = name;
        ReviewNotes = string.IsNullOrEmpty(cleanNotes) ? null : cleanNotes;
        ReviewedAt = now;
        Touch(now);
        return Result.Ok();
    }
}
