using Couples.Domain.Common;

namespace Couples.Domain.Workspaces;

/// <summary>
/// Expiring, single-use invitation to join a workspace. Only the token hash is stored.
/// Retention: kept with the workspace for audit; no personal content.
/// </summary>
public sealed class Invitation : Entity
{
    private Invitation()
    {
    }

    internal Invitation(Guid workspaceId, Guid createdByUserId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        WorkspaceId = workspaceId;
        CreatedByUserId = createdByUserId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        Touch(now);
    }

    public Guid WorkspaceId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsPending(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && now < ExpiresAt;

    internal void MarkAccepted(Guid userId, DateTimeOffset now)
    {
        AcceptedAt = now;
        AcceptedByUserId = userId;
        Touch(now);
    }

    internal void Revoke(DateTimeOffset now)
    {
        RevokedAt = now;
        Touch(now);
    }
}
