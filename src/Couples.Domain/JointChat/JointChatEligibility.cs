using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.JointChat;

/// <summary>
/// An administrator's decision that one workspace may use the joint chat pilot, for example after
/// the couple was recruited and screened. Holds ids, times and a short review reference only.
/// </summary>
[Privacy(DataClassification.Internal, "Kept while the workspace exists; removed with the workspace. The granting admin's id is unlinked if that admin deletes their account.")]
public sealed class JointChatEligibility
{
    public const int ReferenceMaxLength = 64;

    private JointChatEligibility()
    {
    }

    public Guid WorkspaceId { get; private set; }

    /// <summary>Recruitment or review reference (for example a pilot participant code). Never a name.</summary>
    public string Reference { get; private set; } = string.Empty;

    public Guid GrantedByUserId { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static Result<JointChatEligibility> Grant(Guid workspaceId, string? reference, Guid adminUserId, DateTimeOffset now)
    {
        var trimmed = reference?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > ReferenceMaxLength)
        {
            return DomainError.Validation;
        }

        return Result.Ok(new JointChatEligibility { WorkspaceId = workspaceId, Reference = trimmed, GrantedByUserId = adminUserId, GrantedAt = now });
    }

    public void Regrant(string reference, Guid adminUserId, DateTimeOffset now)
    {
        Reference = reference;
        GrantedByUserId = adminUserId;
        GrantedAt = now;
        RevokedAt = null;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    /// <summary>Used when the granting admin deletes their account.</summary>
    public void ForgetGrantingAdmin() => GrantedByUserId = Guid.Empty;
}
