using Couples.Domain.Notifications;
using Couples.Domain.Pace;
using Couples.Domain.Safety;
using Couples.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couples.Infrastructure.Persistence.Configurations;

internal sealed class PacePreferencesConfiguration : IEntityTypeConfiguration<PacePreferences>
{
    public void Configure(EntityTypeBuilder<PacePreferences> builder)
    {
        builder.ToTable("pace_preferences");
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<PacePreferences>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(n => new { n.RecipientUserId, n.VisibleAt });
        builder.HasIndex(n => n.SubjectId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(n => n.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PersonalSafetyConfiguration : IEntityTypeConfiguration<PersonalSafety>
{
    public void Configure(EntityTypeBuilder<PersonalSafety> builder)
    {
        builder.ToTable("personal_safety");
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.AnswersJson).HasMaxLength(2_000);
        builder.Property(s => s.Level).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(s => s.BlocksJointFlows);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<PersonalSafety>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
