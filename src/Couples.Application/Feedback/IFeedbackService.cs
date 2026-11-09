using Couples.Domain.Common;
using Couples.Domain.Feedback;

namespace Couples.Application.Feedback;

public sealed record FeedbackView(Guid Id, DateTimeOffset CreatedAt, FeedbackArea Area, int? Rating, string? Text, bool CanContact);

public interface IFeedbackService
{
    /// <summary>
    /// Saves feedback. Linked to <paramref name="userId"/> only when <paramref name="allowContact"/>
    /// is true. At most <see cref="DailyLimit"/> entries per person per day (<see cref="DomainError.RateLimited"/>).
    /// </summary>
    Task<Result> SubmitAsync(Guid userId, FeedbackArea area, int? rating, string? text, bool allowContact, CancellationToken cancellationToken = default);

    /// <summary>Newest first, for admins.</summary>
    Task<IReadOnlyList<FeedbackView>> ListAsync(int take, CancellationToken cancellationToken = default);

    const int DailyLimit = 5;
}
