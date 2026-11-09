using Couples.Application.Abstractions;
using Couples.Application.Workspaces;
using Couples.Domain.Common;
using Couples.Domain.Workspaces;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Couples.Infrastructure.Workspaces;

internal sealed partial class WorkspaceService(
    CouplesDbContext db,
    IClock clock,
    IOptions<WorkspaceOptions> options,
    ILogger<WorkspaceService> logger) : IWorkspaceService
{
    public async Task<WorkspaceSummary?> GetMyWorkspaceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var workspace = await MemberWorkspaces(userId).FirstOrDefaultAsync(cancellationToken);
        return workspace is null ? null : ToSummary(workspace, userId);
    }

    public async Task<Result<WorkspaceSummary>> GetAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await LoadForMemberAsync(userId, workspaceId, cancellationToken);
        return workspace is null ? DomainError.NotFound : Result.Ok(ToSummary(workspace, userId));
    }

    public async Task<Result<WorkspaceSummary>> CreateAsync(Guid userId, string name, CancellationToken cancellationToken = default)
    {
        if (await HasActiveMembershipAsync(userId, cancellationToken))
        {
            return DomainError.AlreadyInWorkspace;
        }

        var created = RelationshipWorkspace.Create(userId, name, clock.UtcNow);
        if (!created.Succeeded)
        {
            return created.Error!.Value;
        }

        db.Workspaces.Add(created.Value);
        var saved = await SaveAsync(cancellationToken);
        if (!saved.Succeeded)
        {
            return saved.Error!.Value;
        }

        LogWorkspaceCreated(logger, created.Value.Id);
        return Result.Ok(ToSummary(created.Value, userId));
    }

    public async Task<Result<CreatedInvitation>> CreateInvitationAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await LoadForMemberAsync(userId, workspaceId, cancellationToken);
        if (workspace is null)
        {
            return DomainError.NotFound;
        }

        var issued = workspace.CreateInvitation(userId, clock.UtcNow, options.Value.InvitationLifetime);
        if (!issued.Succeeded)
        {
            return issued.Error!.Value;
        }

        var saved = await SaveAsync(cancellationToken);
        if (!saved.Succeeded)
        {
            return saved.Error!.Value;
        }

        var (invitation, rawToken) = issued.Value;
        LogInvitationCreated(logger, workspace.Id, invitation.Id);
        return Result.Ok(new CreatedInvitation(invitation.Id, rawToken, invitation.ExpiresAt));
    }

    public async Task<Result<InvitationPreview>> PreviewInvitationAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var (workspace, invitation) = await FindByTokenAsync(token, cancellationToken);
        if (workspace is null || invitation is null || !invitation.IsPending(clock.UtcNow))
        {
            return DomainError.InvitationInvalid;
        }

        if (invitation.CreatedByUserId == userId)
        {
            return DomainError.CannotAcceptOwnInvitation;
        }

        return Result.Ok(new InvitationPreview(workspace.Name, invitation.ExpiresAt));
    }

    public async Task<Result<WorkspaceSummary>> AcceptInvitationAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var (workspace, invitation) = await FindByTokenAsync(token, cancellationToken);
        if (workspace is null || invitation is null)
        {
            return DomainError.InvitationInvalid;
        }

        if (!workspace.IsActiveMember(userId) && await HasActiveMembershipAsync(userId, cancellationToken))
        {
            return DomainError.AlreadyInWorkspace;
        }

        var accepted = workspace.Accept(invitation, userId, clock.UtcNow);
        if (!accepted.Succeeded)
        {
            return accepted.Error!.Value;
        }

        var saved = await SaveAsync(cancellationToken);
        if (!saved.Succeeded)
        {
            // A concurrent acceptance or join won; this token is no longer usable.
            return saved.Error == DomainError.Conflict ? DomainError.InvitationInvalid : saved.Error!.Value;
        }

        LogInvitationAccepted(logger, workspace.Id, invitation.Id);
        return Result.Ok(ToSummary(workspace, userId));
    }

    public async Task<Result> RevokeInvitationAsync(Guid userId, Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var workspace = await LoadForMemberAsync(userId, workspaceId, cancellationToken);
        if (workspace is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        var revoked = workspace.RevokeInvitation(userId, invitationId, clock.UtcNow);
        return revoked.Succeeded ? await SaveAsync(cancellationToken) : revoked;
    }

    public async Task<Result> LeaveAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await LoadForMemberAsync(userId, workspaceId, cancellationToken);
        if (workspace is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        var left = workspace.Leave(userId, clock.UtcNow);
        if (!left.Succeeded)
        {
            return left;
        }

        var saved = await SaveAsync(cancellationToken);
        if (saved.Succeeded)
        {
            LogMemberLeft(logger, workspace.Id);
        }

        return saved;
    }

    private IQueryable<RelationshipWorkspace> MemberWorkspaces(Guid userId) =>
        db.Workspaces
            .Include(w => w.Memberships)
            .Include(w => w.Invitations)
            .AsSplitQuery()
            .Where(w => w.Memberships.Any(m => m.UserId == userId && m.LeftAt == null));

    private Task<RelationshipWorkspace?> LoadForMemberAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken) =>
        MemberWorkspaces(userId).FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

    private Task<bool> HasActiveMembershipAsync(Guid userId, CancellationToken cancellationToken) =>
        db.WorkspaceMemberships.AnyAsync(m => m.UserId == userId && m.LeftAt == null, cancellationToken);

    private async Task<(RelationshipWorkspace? Workspace, Invitation? Invitation)> FindByTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (!InvitationToken.LooksValid(token))
        {
            return (null, null);
        }

        var hash = InvitationToken.Hash(token);
        var workspace = await db.Workspaces
            .Include(w => w.Memberships)
            .Include(w => w.Invitations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(w => w.Invitations.Any(i => i.TokenHash == hash), cancellationToken);

        return (workspace, workspace?.Invitations.Single(i => i.TokenHash == hash));
    }

    private async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(DomainError.Conflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result.Fail(DomainError.Conflict);
        }
    }

    private WorkspaceSummary ToSummary(RelationshipWorkspace workspace, Guid userId)
    {
        var now = clock.UtcNow;
        var partner = workspace.Memberships.FirstOrDefault(m => m.UserId != userId && m.IsActive);
        var pending = workspace.Invitations
            .Where(i => i.IsPending(now))
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new PendingInvitation(i.Id, i.ExpiresAt))
            .FirstOrDefault();

        return new WorkspaceSummary(workspace.Id, workspace.Name, partner is not null, partner?.JoinedAt, pending);
    }

    // Logs carry identifiers only: never names, emails, tokens or any user-written text.
    [LoggerMessage(Level = LogLevel.Information, Message = "Workspace {WorkspaceId} created")]
    private static partial void LogWorkspaceCreated(ILogger logger, Guid workspaceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invitation {InvitationId} issued for workspace {WorkspaceId}")]
    private static partial void LogInvitationCreated(ILogger logger, Guid workspaceId, Guid invitationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invitation {InvitationId} accepted for workspace {WorkspaceId}")]
    private static partial void LogInvitationAccepted(ILogger logger, Guid workspaceId, Guid invitationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "A member left workspace {WorkspaceId}")]
    private static partial void LogMemberLeft(ILogger logger, Guid workspaceId);
}
