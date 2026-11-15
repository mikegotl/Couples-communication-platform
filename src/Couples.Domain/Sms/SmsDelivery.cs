using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Sms;

/// <summary>
/// That a text was (or was deliberately not) sent for one in-app notification. Claimed before
/// sending so a notification is texted at most once, even with several app instances. Ids, states
/// and the provider's message id only; never the number or the text.
/// </summary>
[Privacy(DataClassification.Personal, "Purged 90 days after creation; deleted with the account.")]
public sealed class SmsDelivery : Entity
{
    private SmsDelivery()
    {
    }

    public Guid NotificationId { get; private set; }

    public Guid RecipientUserId { get; private set; }

    public SmsDeliveryStatus Status { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public static SmsDelivery Claim(Guid notificationId, Guid recipientUserId, DateTimeOffset now)
    {
        var delivery = new SmsDelivery { NotificationId = notificationId, RecipientUserId = recipientUserId, Status = SmsDeliveryStatus.Sending };
        delivery.Touch(now);
        return delivery;
    }

    public static SmsDelivery Skip(Guid notificationId, Guid recipientUserId, DateTimeOffset now)
    {
        var delivery = new SmsDelivery { NotificationId = notificationId, RecipientUserId = recipientUserId, Status = SmsDeliveryStatus.Skipped };
        delivery.Touch(now);
        return delivery;
    }

    public void Sent(string? providerMessageId, DateTimeOffset now)
    {
        Status = SmsDeliveryStatus.Sent;
        ProviderMessageId = providerMessageId is { Length: > 64 } ? providerMessageId[..64] : providerMessageId;
        Touch(now);
    }

    public void Failed(DateTimeOffset now)
    {
        Status = SmsDeliveryStatus.Failed;
        Touch(now);
    }
}

public enum SmsDeliveryStatus
{
    Sending = 1,
    Sent = 2,
    Failed = 3,

    /// <summary>Not sent on purpose (daily cap reached, or the recipient can't get texts right now).</summary>
    Skipped = 4,
}
