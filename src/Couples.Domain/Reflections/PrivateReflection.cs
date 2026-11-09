using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Reflections;

/// <summary>
/// A private reflection. Owned by exactly one user and visible only to them, whatever workspace
/// they are in. There is no path from a reflection to a partner except the user copying words
/// into a draft and explicitly sharing that draft.
/// </summary>
[Privacy(DataClassification.PrivateSensitive, "Kept until the owner deletes it or deletes their account; erased by the deletion workflow.")]
public sealed class PrivateReflection : Entity
{
    public const int MaxTitleLength = 120;
    public const int MaxBodyLength = 20_000;

    private PrivateReflection()
    {
    }

    public Guid OwnerUserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public static Result<PrivateReflection> Create(Guid ownerUserId, string? title, string? body, DateTimeOffset now)
    {
        if (ownerUserId == Guid.Empty)
        {
            return DomainError.Validation;
        }

        var reflection = new PrivateReflection { OwnerUserId = ownerUserId };
        var updated = reflection.Update(title, body, now);
        return updated.Succeeded ? Result.Ok(reflection) : updated.Error!.Value;
    }

    public Result Update(string? title, string? body, DateTimeOffset now)
    {
        var cleanTitle = title?.Trim() ?? string.Empty;
        var cleanBody = body ?? string.Empty;
        if (cleanTitle.Length > MaxTitleLength || cleanBody.Length > MaxBodyLength || (cleanTitle.Length == 0 && cleanBody.Trim().Length == 0))
        {
            return Result.Fail(DomainError.Validation);
        }

        Title = cleanTitle;
        Body = cleanBody;
        Touch(now);
        return Result.Ok();
    }
}
