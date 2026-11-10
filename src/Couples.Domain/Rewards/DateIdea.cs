using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Rewards;

/// <summary>
/// A date suggestion shown beside the rewards: something to do together, or a local spot worth
/// trying. Only an idea: no voucher, no credits, and the place has agreed to nothing.
/// </summary>
[Privacy(DataClassification.Internal, "Reference content managed by admins; kept until an admin removes it.")]
public sealed class DateIdea : Entity
{
    public const int MaxTitleLength = 80;
    public const int MaxDescriptionLength = 300;

    private DateIdea()
    {
    }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Town or area it is in, or null for an idea that works anywhere.</summary>
    public string? Area { get; private set; }

    public DateIdeaKind Kind { get; private set; }

    /// <summary>Optional link to the place's own site.</summary>
    public string? Url { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<DateIdea> Create(DateIdeaDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        var title = details.Title?.Trim() ?? string.Empty;
        var description = details.Description?.Trim() ?? string.Empty;
        var area = string.IsNullOrWhiteSpace(details.Area) ? null : details.Area.Trim();
        var url = string.IsNullOrWhiteSpace(details.Url) ? null : details.Url.Trim();
        if (title.Length is 0 or > MaxTitleLength
            || description.Length is 0 or > MaxDescriptionLength
            || area is { Length: > CoupleRewardAccount.MaxAreaLength }
            || (url is not null && !(RewardPartner.IsWebAddress(url) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            || !Enum.IsDefined(details.Kind))
        {
            return DomainError.Validation;
        }

        var idea = new DateIdea { Title = title, Description = description, Area = area, Kind = details.Kind, Url = url, IsActive = true };
        idea.Touch(now);
        return Result.Ok(idea);
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        IsActive = active;
        Touch(now);
    }

    public bool AvailableIn(string? area) =>
        Area is null || (area is not null && string.Equals(Area.Trim(), area.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record DateIdeaDetails(string? Title, string? Description, string? Area, DateIdeaKind Kind, string? Url);

public enum DateIdeaKind
{
    Outdoors = 1,
    FoodAndDrink = 2,
    AtHome = 3,
    Activity = 4,
    Culture = 5,
}
