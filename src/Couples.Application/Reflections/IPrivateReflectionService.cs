using Couples.Domain.Common;

namespace Couples.Application.Reflections;

/// <summary>
/// Owner-only access to private reflections. Every query is filtered by the acting user's id;
/// another user's reflection id behaves exactly like an id that does not exist.
/// </summary>
public interface IPrivateReflectionService
{
    Task<Result<ReflectionView>> CreateAsync(Guid userId, string? title, string? body, CancellationToken cancellationToken = default);

    Task<Result<ReflectionView>> UpdateAsync(Guid userId, Guid reflectionId, string? title, string? body, CancellationToken cancellationToken = default);

    Task<Result<ReflectionView>> GetAsync(Guid userId, Guid reflectionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReflectionView>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid userId, Guid reflectionId, CancellationToken cancellationToken = default);
}

public sealed record ReflectionView(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
