using Couples.Domain.Feedback;
using Couples.Domain.Pilot;
using Couples.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couples.Infrastructure.Persistence.Configurations;

internal sealed class PilotEnrollmentConfiguration : IEntityTypeConfiguration<PilotEnrollment>
{
    public void Configure(EntityTypeBuilder<PilotEnrollment> builder)
    {
        builder.ToTable("pilot_enrollments");
        builder.HasKey(e => e.UserId);
        builder.Property(e => e.TermsVersion).HasMaxLength(64);
        builder.Property(e => e.PrivacyNoticeVersion).HasMaxLength(64);
        builder.Ignore(e => e.IsCurrent);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<PilotEnrollment>(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FeedbackEntryConfiguration : IEntityTypeConfiguration<FeedbackEntry>
{
    public void Configure(EntityTypeBuilder<FeedbackEntry> builder)
    {
        builder.ToTable("feedback");
        builder.Property(f => f.Area).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Text).HasMaxLength(FeedbackEntry.MaxTextLength);
        builder.HasIndex(f => f.CreatedAt);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(f => f.ContactUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
