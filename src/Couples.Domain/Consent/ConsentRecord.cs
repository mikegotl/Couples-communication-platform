using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Consent;

/// <summary>
/// One explicit decision by a user, kept as a ledger entry. Records what was consented to (purpose
/// and subject id) and a fingerprint of the exact content, never the content itself.
/// </summary>
[Privacy(DataClassification.Personal, "Kept for the life of the account as evidence of consent; deleted with the account.")]
public sealed class ConsentRecord : Entity
{
    private ConsentRecord()
    {
    }

    public Guid UserId { get; private set; }

    public Guid? WorkspaceId { get; private set; }

    public ConsentPurpose Purpose { get; private set; }

    /// <summary>The thing consented to, for example the draft being shared.</summary>
    public Guid SubjectId { get; private set; }

    /// <summary>SHA-256 of the approved content, so the ledger proves what was approved without storing it.</summary>
    public string? ContentFingerprint { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? WithdrawnAt { get; private set; }

    public static ConsentRecord Grant(Guid userId, Guid? workspaceId, ConsentPurpose purpose, Guid subjectId, string? contentFingerprint, DateTimeOffset now)
    {
        var record = new ConsentRecord
        {
            UserId = userId,
            WorkspaceId = workspaceId,
            Purpose = purpose,
            SubjectId = subjectId,
            ContentFingerprint = contentFingerprint,
            GrantedAt = now,
        };
        record.Touch(now);
        return record;
    }

    public void Withdraw(DateTimeOffset now)
    {
        WithdrawnAt ??= now;
        Touch(now);
    }
}

public enum ConsentPurpose
{
    /// <summary>Share one specific message with one specific partner.</summary>
    ShareMessage = 1,
}
