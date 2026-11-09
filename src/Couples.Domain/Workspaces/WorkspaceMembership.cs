using Couples.Domain.Common;

namespace Couples.Domain.Workspaces;

/// <summary>
/// A user's membership in a workspace. Membership never grants access to another member's
/// private content. Retention: kept while the workspace exists so history stays attributable;
/// removed with the workspace or the user's account deletion.
/// </summary>
public sealed class WorkspaceMembership : Entity
{
    private WorkspaceMembership()
    {
    }

    internal WorkspaceMembership(Guid workspaceId, Guid userId, WorkspaceRole role, DateTimeOffset now)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
        JoinedAt = now;
        Touch(now);
    }

    public Guid WorkspaceId { get; private set; }

    public Guid UserId { get; private set; }

    public WorkspaceRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>Set when the member withdraws. A withdrawn member has no access.</summary>
    public DateTimeOffset? LeftAt { get; private set; }

    public bool IsActive => LeftAt is null;

    internal void Leave(DateTimeOffset now)
    {
        LeftAt = now;
        Touch(now);
    }
}

public enum WorkspaceRole
{
    Creator = 1,
    Partner = 2,
}
