using Couples.Domain.Common;

namespace Couples.Application.Workspaces;

/// <summary>
/// Workspace, invitation and membership use cases. Every method takes the acting user's id
/// and enforces membership itself; callers must not rely on the UI to restrict access.
/// A workspace the user is not an active member of is reported as <see cref="DomainError.NotFound"/>
/// so its existence is not revealed.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>The user's active workspace, or null. Phase 1 allows one active workspace per user.</summary>
    Task<WorkspaceSummary?> GetMyWorkspaceAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<WorkspaceSummary>> GetAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);

    Task<Result<WorkspaceSummary>> CreateAsync(Guid userId, string name, CancellationToken cancellationToken = default);

    /// <summary>Issues a new single-use invitation, revoking any other pending one. The token is returned once.</summary>
    Task<Result<CreatedInvitation>> CreateInvitationAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Checks an invitation token without consuming it.</summary>
    Task<Result<InvitationPreview>> PreviewInvitationAsync(Guid userId, string token, CancellationToken cancellationToken = default);

    Task<Result<WorkspaceSummary>> AcceptInvitationAsync(Guid userId, string token, CancellationToken cancellationToken = default);

    Task<Result> RevokeInvitationAsync(Guid userId, Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the user from the workspace. Access ends immediately.</summary>
    Task<Result> LeaveAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
}

/// <summary>What one member may see about their workspace. Contains no partner content.</summary>
public sealed record WorkspaceSummary(
    Guid Id,
    string Name,
    bool PartnerJoined,
    DateTimeOffset? PartnerJoinedAt,
    PendingInvitation? PendingInvitation);

public sealed record PendingInvitation(Guid Id, DateTimeOffset ExpiresAt);

public sealed record CreatedInvitation(Guid Id, string Token, DateTimeOffset ExpiresAt);

public sealed record InvitationPreview(string WorkspaceName, DateTimeOffset ExpiresAt);
