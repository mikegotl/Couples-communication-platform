using Couples.Domain.Messaging;
using Couples.Infrastructure.Persistence;

namespace Couples.Infrastructure.Messaging;

internal static class MessageAccess
{
    /// <summary>
    /// What a recipient may read: addressed to them, not revoked, and both they and the sender
    /// are still active members of the workspace. Leaving (by either partner) ends access.
    /// </summary>
    public static IQueryable<SharedMessage> ReadableByRecipient(CouplesDbContext db, Guid userId) =>
        db.SharedMessages.Where(m =>
            m.RecipientUserId == userId
            && m.RevokedAt == null
            && db.WorkspaceMemberships.Any(r => r.WorkspaceId == m.WorkspaceId && r.UserId == userId && r.LeftAt == null)
            && db.WorkspaceMemberships.Any(s => s.WorkspaceId == m.WorkspaceId && s.UserId == m.SenderUserId && s.LeftAt == null));
}
