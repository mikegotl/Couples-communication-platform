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
    /// Erases the user's app data for every pending request (reflections, drafts, consent records,
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
    IReadOnlyList<ExportedConsent> Consents);

public sealed record ExportedMembership(Guid WorkspaceId, string WorkspaceName, string Role, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

public sealed record ExportedReflection(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ExportedDraft(Guid Id, string Body, string Status, DateTimeOffset UpdatedAt);

public sealed record ExportedMessage(Guid Id, string Body, DateTimeOffset SharedAt, DateTimeOffset? RevokedAt);

public sealed record ExportedConsent(Guid Id, string Purpose, Guid SubjectId, DateTimeOffset GrantedAt, DateTimeOffset? WithdrawnAt);
