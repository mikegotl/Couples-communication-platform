using System.Text.Json;
using Couples.Application.Abstractions;
using Couples.Application.Safety;
using Couples.Domain.Common;
using Couples.Domain.Safety;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Safety;

/// <summary>
/// Deliberately has no logger: nothing about a safety check or safety mode is ever logged.
/// </summary>
internal sealed class SafetyService(CouplesDbContext db, IClock clock) : ISafetyService, ISafetyGate
{
    public async Task<SafetyStatus> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ToStatus(await db.PersonalSafety.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken));

    public async Task<Result<SafetyStatus>> RecordCheckAsync(Guid userId, IReadOnlyDictionary<string, string> answers, CancellationToken cancellationToken = default)
    {
        var safety = await LoadAsync(userId, cancellationToken);
        var recorded = safety.RecordCheck(answers, clock.UtcNow);
        if (!recorded.Succeeded)
        {
            return recorded.Error!.Value;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(ToStatus(safety));
    }

    public async Task<SafetyStatus> DeleteAnswersAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var safety = await LoadAsync(userId, cancellationToken);
        safety.ClearCheck(clock.UtcNow);
        if (!safety.SafetyMode)
        {
            // Nothing left worth keeping: remove the row entirely.
            db.PersonalSafety.Remove(safety);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToStatus(safety.SafetyMode ? safety : null);
    }

    public async Task<SafetyStatus> SetSafetyModeAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default)
    {
        var safety = await LoadAsync(userId, cancellationToken);
        safety.SetSafetyMode(enabled, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return ToStatus(safety);
    }

    public Task<bool> UserBlocksJointFlowsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.PersonalSafety.AnyAsync(s => s.UserId == userId && (s.SafetyMode || s.Level == SafetyLevel.Unsafe), cancellationToken);

    public async Task<bool> WorkspaceAllowsJointFlowsAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        !await db.WorkspaceMemberships
            .Where(m => m.WorkspaceId == workspaceId && m.LeftAt == null)
            .AnyAsync(m => db.PersonalSafety.Any(s => s.UserId == m.UserId && (s.SafetyMode || s.Level == SafetyLevel.Unsafe)), cancellationToken);

    private async Task<PersonalSafety> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var safety = await db.PersonalSafety.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (safety is null)
        {
            safety = PersonalSafety.For(userId);
            db.PersonalSafety.Add(safety);
        }

        return safety;
    }

    private static SafetyStatus ToStatus(PersonalSafety? s) => s is null
        ? new SafetyStatus(null, null, false, null, false, false)
        : new SafetyStatus(
            s.AnswersJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(s.AnswersJson),
            s.Level,
            s.SelfHarmSupport,
            s.CheckedAt,
            s.SafetyMode,
            s.BlocksJointFlows);
}
