using Couples.Application;
using Couples.Application.Abstractions;
using Couples.Application.JointChat;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.JointChat;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Couples.Infrastructure.JointChat;

/// <summary>Per-workspace eligibility. Checks the Admin role itself; shows ids and counts, never names or content.</summary>
internal sealed class JointChatAdminService(
    CouplesDbContext db,
    IClock clock,
    IFeatureFlags flags,
    IOptions<JointChatOptions> options,
    IJointChatNotifier notifier) : IJointChatAdminService
{
    private const string AdminRole = "ADMIN";

    public async Task<Result<JointChatAdminOverview>> GetOverviewAsync(Guid adminUserId, CancellationToken cancellationToken = default)
    {
        if (!await AllowedAsync(adminUserId, cancellationToken))
        {
            return DomainError.NotFound;
        }

        var workspaces = await db.JointChatEligibility.AsNoTracking()
            .Where(e => e.RevokedAt == null)
            .OrderBy(e => e.GrantedAt)
            .Select(e => new EligibleWorkspace(
                e.WorkspaceId,
                e.Reference,
                e.GrantedAt,
                db.JointChatSessions.Any(s => s.WorkspaceId == e.WorkspaceId && s.Status != JointChatStatus.Ended)))
            .ToListAsync(cancellationToken);
        return Result.Ok(new JointChatAdminOverview(true, options.Value.ClinicalSignOff, options.Value.SecuritySignOff, workspaces));
    }

    public async Task<Result> GrantAsync(Guid adminUserId, Guid workspaceId, string? reference, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        if (!await AllowedAsync(adminUserId, cancellationToken))
        {
            return Result.Fail(DomainError.NotFound);
        }

        if (!options.Value.SignedOff)
        {
            // No workspace can be made eligible until both independent reviews are recorded.
            return Result.Fail(DomainError.Conflict);
        }

        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken))
        {
            return Result.Fail(DomainError.NotFound);
        }

        var now = clock.UtcNow;
        var granted = JointChatEligibility.Grant(workspaceId, reference, adminUserId, now);
        if (!granted.Succeeded)
        {
            return granted;
        }

        var existing = await db.JointChatEligibility.FirstOrDefaultAsync(e => e.WorkspaceId == workspaceId, cancellationToken);
        if (existing is null)
        {
            db.JointChatEligibility.Add(granted.Value);
        }
        else
        {
            existing.Regrant(granted.Value.Reference, adminUserId, now);
        }

        db.AuditEvents.Add(AuditEvent.Record(adminUserId, AuditActions.JointChatEligibilityGranted, workspaceId, null, now));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> RevokeAsync(Guid adminUserId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        if (!await AllowedAsync(adminUserId, cancellationToken))
        {
            return Result.Fail(DomainError.NotFound);
        }

        var eligibility = await db.JointChatEligibility.FirstOrDefaultAsync(e => e.WorkspaceId == workspaceId && e.RevokedAt == null, cancellationToken);
        if (eligibility is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        var now = clock.UtcNow;
        eligibility.Revoke(now);
        var open = await db.JointChatSessions.Where(s => s.WorkspaceId == workspaceId && s.Status != JointChatStatus.Ended).ToListAsync(cancellationToken);
        foreach (var session in open)
        {
            session.EndUnavailable(now);
        }

        db.AuditEvents.Add(AuditEvent.Record(adminUserId, AuditActions.JointChatEligibilityRevoked, workspaceId, null, now));
        await db.SaveChangesAsync(cancellationToken);
        foreach (var session in open)
        {
            notifier.Notify(session.Id);
        }

        return Result.Ok();
    }

    private async Task<bool> AllowedAsync(Guid adminUserId, CancellationToken cancellationToken) =>
        await flags.IsEnabledAsync(FeatureFlags.Phase2JointChat, cancellationToken)
        && await db.UserRoles.AnyAsync(ur => ur.UserId == adminUserId && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == AdminRole), cancellationToken);
}
