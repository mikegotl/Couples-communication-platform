using Couples.Application.Abstractions;
using Couples.Application.Reflections;
using Couples.Domain.Common;
using Couples.Domain.Reflections;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Reflections;

internal sealed class PrivateReflectionService(CouplesDbContext db, IClock clock) : IPrivateReflectionService
{
    public async Task<Result<ReflectionView>> CreateAsync(Guid userId, string? title, string? body, CancellationToken cancellationToken = default)
    {
        var created = PrivateReflection.Create(userId, title, body, clock.UtcNow);
        if (!created.Succeeded)
        {
            return created.Error!.Value;
        }

        db.PrivateReflections.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(ToView(created.Value));
    }

    public async Task<Result<ReflectionView>> UpdateAsync(Guid userId, Guid reflectionId, string? title, string? body, CancellationToken cancellationToken = default)
    {
        var reflection = await Owned(userId).FirstOrDefaultAsync(r => r.Id == reflectionId, cancellationToken);
        if (reflection is null)
        {
            return DomainError.NotFound;
        }

        var updated = reflection.Update(title, body, clock.UtcNow);
        if (!updated.Succeeded)
        {
            return updated.Error!.Value;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(ToView(reflection));
    }

    public async Task<Result<ReflectionView>> GetAsync(Guid userId, Guid reflectionId, CancellationToken cancellationToken = default)
    {
        var reflection = await Owned(userId).AsNoTracking().FirstOrDefaultAsync(r => r.Id == reflectionId, cancellationToken);
        return reflection is null ? DomainError.NotFound : Result.Ok(ToView(reflection));
    }

    public async Task<IReadOnlyList<ReflectionView>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var reflections = await Owned(userId).AsNoTracking().OrderByDescending(r => r.UpdatedAt).ToListAsync(cancellationToken);
        return [.. reflections.Select(ToView)];
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid reflectionId, CancellationToken cancellationToken = default)
    {
        var deleted = await Owned(userId).Where(r => r.Id == reflectionId).ExecuteDeleteAsync(cancellationToken);
        return deleted == 1 ? Result.Ok() : Result.Fail(DomainError.NotFound);
    }

    /// <summary>The only entry point to reflections: always scoped to the owner.</summary>
    private IQueryable<PrivateReflection> Owned(Guid userId) => db.PrivateReflections.Where(r => r.OwnerUserId == userId);

    private static ReflectionView ToView(PrivateReflection r) => new(r.Id, r.Title, r.Body, r.CreatedAt, r.UpdatedAt);
}
