using Couples.Domain.Sms;
using Couples.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couples.Infrastructure.Persistence.Configurations;

internal sealed class SmsSettingsConfiguration : IEntityTypeConfiguration<SmsSettings>
{
    public void Configure(EntityTypeBuilder<SmsSettings> builder)
    {
        builder.ToTable("sms_settings");
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.PhoneE164).HasMaxLength(16).IsRequired();
        builder.Property(s => s.CodeHash).HasMaxLength(64);
        builder.Property(s => s.PendingFingerprint).HasMaxLength(64);
        builder.Ignore(s => s.IsVerified);
        builder.Ignore(s => s.CanReceive);

        // A number belongs to one account once verified; inbound texts are matched on it.
        builder.HasIndex(s => s.PhoneE164).IsUnique().HasFilter("\"VerifiedAt\" IS NOT NULL");
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<SmsSettings>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SmsDeliveryConfiguration : IEntityTypeConfiguration<SmsDelivery>
{
    public void Configure(EntityTypeBuilder<SmsDelivery> builder)
    {
        builder.ToTable("sms_deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.ProviderMessageId).HasMaxLength(64);

        // The claim: a notification is texted at most once, whichever instance gets there first.
        builder.HasIndex(d => d.NotificationId).IsUnique();
        builder.HasIndex(d => new { d.RecipientUserId, d.CreatedAt });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
