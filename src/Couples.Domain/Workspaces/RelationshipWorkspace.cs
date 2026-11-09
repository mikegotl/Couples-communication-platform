using Couples.Domain.Common;

namespace Couples.Domain.Workspaces;

/// <summary>
/// A shared space for two partners. Holds membership and invitations only; each partner's
/// private content is owned by that partner, never by the workspace.
/// Retention: deleted when the last member leaves and the deletion workflow runs (milestone 03).
/// </summary>
public sealed class RelationshipWorkspace : Entity
{
    public const int MaxActiveMembers = 2;
    public const int MaxNameLength = 80;

    private readonly List<WorkspaceMembership> memberships = [];
    private readonly List<Invitation> invitations = [];

    private RelationshipWorkspace()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public Guid CreatedByUserId { get; private set; }

    public IReadOnlyCollection<WorkspaceMembership> Memberships => memberships.AsReadOnly();

    public IReadOnlyCollection<Invitation> Invitations => invitations.AsReadOnly();

    public static Result<RelationshipWorkspace> Create(Guid creatorUserId, string? name, DateTimeOffset now)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (creatorUserId == Guid.Empty || trimmed.Length is 0 or > MaxNameLength)
        {
            return DomainError.Validation;
        }

        var workspace = new RelationshipWorkspace { Name = trimmed, CreatedByUserId = creatorUserId };
        workspace.Touch(now);
        workspace.memberships.Add(new WorkspaceMembership(workspace.Id, creatorUserId, WorkspaceRole.Creator, now));
        return Result.Ok(workspace);
    }

    public bool IsActiveMember(Guid userId) => memberships.Any(m => m.UserId == userId && m.IsActive);

    public int ActiveMemberCount => memberships.Count(m => m.IsActive);

    /// <summary>
    /// Issues a new invitation and revokes any other pending one, so at most one invite link is
    /// live at a time. Returns the raw token, which the caller shows once and must not store.
    /// </summary>
    public Result<(Invitation Invitation, string RawToken)> CreateInvitation(Guid inviterUserId, DateTimeOffset now, TimeSpan lifetime)
    {
        if (!IsActiveMember(inviterUserId))
        {
            return DomainError.NotFound;
        }

        if (ActiveMemberCount >= MaxActiveMembers)
        {
            return DomainError.WorkspaceFull;
        }

        if (lifetime <= TimeSpan.Zero)
        {
            return DomainError.Validation;
        }

        foreach (var pending in invitations.Where(i => i.IsPending(now)))
        {
            pending.Revoke(now);
        }

        var raw = InvitationToken.Generate();
        var invitation = new Invitation(Id, inviterUserId, InvitationToken.Hash(raw), now, now.Add(lifetime));
        invitations.Add(invitation);
        Touch(now);
        return Result.Ok((invitation, raw));
    }

    /// <summary>
    /// Accepts an invitation. The caller must already have checked that the user has no other
    /// active workspace. Single use: a consumed, revoked or expired invitation is rejected.
    /// </summary>
    public Result Accept(Invitation invitation, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        if (invitation.WorkspaceId != Id || !invitation.IsPending(now))
        {
            return Result.Fail(DomainError.InvitationInvalid);
        }

        if (invitation.CreatedByUserId == userId)
        {
            return Result.Fail(DomainError.CannotAcceptOwnInvitation);
        }

        if (IsActiveMember(userId))
        {
            return Result.Fail(DomainError.AlreadyInWorkspace);
        }

        if (ActiveMemberCount >= MaxActiveMembers)
        {
            return Result.Fail(DomainError.WorkspaceFull);
        }

        invitation.MarkAccepted(userId, now);
        memberships.Add(new WorkspaceMembership(Id, userId, WorkspaceRole.Partner, now));
        Touch(now);
        return Result.Ok();
    }

    public Result RevokeInvitation(Guid actingUserId, Guid invitationId, DateTimeOffset now)
    {
        if (!IsActiveMember(actingUserId))
        {
            return Result.Fail(DomainError.NotFound);
        }

        var invitation = invitations.FirstOrDefault(i => i.Id == invitationId);
        if (invitation is null || !invitation.IsPending(now))
        {
            return Result.Fail(DomainError.NotFound);
        }

        invitation.Revoke(now);
        Touch(now);
        return Result.Ok();
    }

    /// <summary>
    /// Withdraws a member. Their access ends immediately and invitations they issued are revoked.
    /// </summary>
    public Result Leave(Guid userId, DateTimeOffset now)
    {
        var membership = memberships.FirstOrDefault(m => m.UserId == userId && m.IsActive);
        if (membership is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        membership.Leave(now);
        foreach (var pending in invitations.Where(i => i.CreatedByUserId == userId && i.IsPending(now)))
        {
            pending.Revoke(now);
        }

        Touch(now);
        return Result.Ok();
    }
}
