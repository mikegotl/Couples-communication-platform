using Couples.Domain.Intake;
using Couples.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couples.Infrastructure.Persistence.Configurations;

internal sealed class ConcernCategoryConfiguration : IEntityTypeConfiguration<ConcernCategory>
{
    public void Configure(EntityTypeBuilder<ConcernCategory> builder)
    {
        builder.ToTable("concern_categories");
        builder.HasKey(c => c.Key);
        builder.Property(c => c.Key).HasMaxLength(ConcernCategory.MaxKeyLength);
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(400).IsRequired();
        builder.Property(c => c.ContentWarning).HasMaxLength(600);

        // The catalog lives in code; seeding keeps the table in step through migrations.
        builder.HasData(ConcernCategory.All.Select(c => new
        {
            c.Key,
            c.Name,
            c.Description,
            c.ContentWarning,
            c.AllowsCustomLabel,
            c.SortOrder,
        }));
    }
}

internal sealed class IntakeProfileConfiguration : IEntityTypeConfiguration<IntakeProfile>
{
    public void Configure(EntityTypeBuilder<IntakeProfile> builder)
    {
        builder.ToTable("intake_profiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property<uint>("xmin").IsRowVersion();

        // One intake per user, even if two requests race to create it.
        builder.HasIndex(p => p.OwnerUserId).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.OwnerUserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Selections).WithOne().HasForeignKey(s => s.IntakeProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Selections).HasField("selections").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ConcernSelectionConfiguration : IEntityTypeConfiguration<ConcernSelection>
{
    public void Configure(EntityTypeBuilder<ConcernSelection> builder)
    {
        builder.ToTable("concern_selections");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.CategoryKey).HasMaxLength(ConcernCategory.MaxKeyLength).IsRequired();
        builder.Property(s => s.CustomLabel).HasMaxLength(ConcernSelection.MaxCustomLabelLength);
        builder.Property(s => s.Summary).HasMaxLength(ConcernSelection.MaxSummaryLength).IsRequired();
        builder.HasIndex(s => s.OwnerUserId);
        builder.HasOne<ConcernCategory>().WithMany().HasForeignKey(s => s.CategoryKey).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
