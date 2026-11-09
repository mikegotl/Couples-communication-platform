using Couples.Domain.Coaching;
using Couples.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couples.Infrastructure.Persistence.Configurations;

internal sealed class CoachingUsageConfiguration : IEntityTypeConfiguration<CoachingUsage>
{
    public void Configure(EntityTypeBuilder<CoachingUsage> builder)
    {
        builder.ToTable("coaching_usage");
        builder.HasKey(u => new { u.UserId, u.Day });
        builder.HasIndex(u => u.Day);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
