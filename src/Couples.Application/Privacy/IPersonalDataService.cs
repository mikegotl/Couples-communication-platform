namespace Couples.Application.Privacy;

/// <summary>Export and erasure of one user's own data.</summary>
public interface IPersonalDataService
{
    /// <summary>
    /// Everything the app holds that belongs to or was shared with the user. Never includes a
    /// partner's reflections, drafts or consent decisions.
    /// </summary>
    Task<PersonalDataExport> ExportAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Records a deletion request. Returns its id.</summary>
    Task<Guid> RequestDeletionAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases the user's app data for every pending request (reflections, intake, exercises, drafts, consent records,
    /// memberships, text of messages they sent) and then their account. Safe to run repeatedly.
    /// Returns the number of requests completed.
    /// </summary>
    Task<int> ProcessPendingDeletionsAsync(CancellationToken cancellationToken = default);
}

public sealed record PersonalDataExport(
    DateTimeOffset ExportedAt,
    Guid UserId,
    IReadOnlyList<ExportedMembership> Memberships,
    IReadOnlyList<ExportedReflection> Reflections,
    IReadOnlyList<ExportedDraft> Drafts,
    IReadOnlyList<ExportedMessage> MessagesSent,
    IReadOnlyList<ExportedMessage> MessagesReceived,
    IReadOnlyList<ExportedConsent> Consents,
    ExportedIntake? Intake,
    IReadOnlyList<ExportedExerciseAttempt> ExerciseAttempts,
    IReadOnlyList<ExportedCoachingUsage> CoachingUsage,
    ExportedPace? Pace,
    IReadOnlyList<ExportedNotification> Notifications,
    ExportedSafety? Safety,
    ExportedPilot? Pilot = null,
    IReadOnlyList<ExportedFeedback>? Feedback = null,
    IReadOnlyList<ExportedJointChat>? JointChats = null,
    IReadOnlyList<ExportedRewardRedemption>? RewardsRedeemed = null);

/// <summary>A date reward the user cashed their couple's credits in for.</summary>
public sealed record ExportedRewardRedemption(Guid WorkspaceId, string OfferTitle, string PartnerName, int CostCredits, string VoucherCode, DateTimeOffset RedeemedAt);

/// <summary>A joint chat the user took part in. The partner's lines are included only while both are still in the workspace, as for received messages.</summary>
public sealed record ExportedJointChat(Guid Id, DateTimeOffset InvitedAt, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, IReadOnlyList<ExportedJointChatLine> Lines);

public sealed record ExportedJointChatLine(bool Mine, string Body, DateTimeOffset SentAt);

public sealed record ExportedPilot(string TermsVersion, string PrivacyNoticeVersion, DateTimeOffset ConsentedAt, DateTimeOffset? WithdrawnAt);

public sealed record ExportedFeedback(DateTimeOffset CreatedAt, string Area, int? Rating, string? Text);

public sealed record ExportedMembership(Guid WorkspaceId, string WorkspaceName, string Role, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

public sealed record ExportedReflection(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ExportedDraft(Guid Id, string Body, string Status, DateTimeOffset UpdatedAt);

public sealed record ExportedMessage(Guid Id, string Body, DateTimeOffset SharedAt, DateTimeOffset? RevokedAt);

public sealed record ExportedConsent(Guid Id, string Purpose, Guid SubjectId, DateTimeOffset GrantedAt, DateTimeOffset? WithdrawnAt);

public sealed record ExportedIntake(string Status, DateTimeOffset? CompletedAt, DateTimeOffset? SkippedAt, IReadOnlyList<ExportedConcern> Concerns);

public sealed record ExportedConcern(Guid Id, string Category, string? CustomLabel, string Summary, DateTimeOffset? WarningAcknowledgedAt, DateTimeOffset UpdatedAt);

public sealed record ExportedExerciseAttempt(Guid Id, string Exercise, int Version, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, IReadOnlyDictionary<string, string> Answers);

public sealed record ExportedCoachingUsage(DateOnly Day, int Requests, long InputTokens, long OutputTokens);

public sealed record ExportedPace(TimeOnly? QuietStart, TimeOnly? QuietEnd, string TimeZoneId, bool Muted, DateTimeOffset? PausedUntil);

public sealed record ExportedNotification(string Kind, Guid MessageId, DateTimeOffset VisibleAt, DateTimeOffset? ReadAt);

public sealed record ExportedSafety(IReadOnlyDictionary<string, string>? Answers, string? Level, int? ContentVersion, DateTimeOffset? CheckedAt, bool SafetyMode);
