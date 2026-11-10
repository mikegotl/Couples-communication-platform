using Couples.Domain.Rewards;
using Couples.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couples.Infrastructure.Persistence.Configurations;

internal sealed class CoupleRewardAccountConfiguration : IEntityTypeConfiguration<CoupleRewardAccount>
{
    public void Configure(EntityTypeBuilder<CoupleRewardAccount> builder)
    {
        builder.ToTable("reward_accounts");
        builder.HasKey(a => a.WorkspaceId);
        builder.Property(a => a.Area).HasMaxLength(CoupleRewardAccount.MaxAreaLength);

        // Both partners can open the dashboard or cash in at once; only one change to the balance wins.
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasOne<RelationshipWorkspace>().WithOne().HasForeignKey<CoupleRewardAccount>(a => a.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CreditEntryConfiguration : IEntityTypeConfiguration<CreditEntry>
{
    public void Configure(EntityTypeBuilder<CreditEntry> builder)
    {
        builder.ToTable("credit_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => new { e.WorkspaceId, e.CreatedAt });

        // A week is turned into credits at most once per workspace, even if both partners load at once.
        builder.HasIndex(e => new { e.WorkspaceId, e.WeekStart }).IsUnique()
            .HasFilter("\"Kind\" = 'WeeklyEarned'").HasDatabaseName("ux_credit_entries_week");
        builder.HasOne<RelationshipWorkspace>().WithMany().HasForeignKey(e => e.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RewardOfferConfiguration : IEntityTypeConfiguration<RewardOffer>
{
    public void Configure(EntityTypeBuilder<RewardOffer> builder)
    {
        builder.ToTable("reward_offers");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Title).HasMaxLength(RewardOffer.MaxTitleLength).IsRequired();
        builder.Property(o => o.PartnerName).HasMaxLength(RewardOffer.MaxPartnerLength).IsRequired();
        builder.Property(o => o.Description).HasMaxLength(RewardOffer.MaxDescriptionLength).IsRequired();
        builder.Property(o => o.Area).HasMaxLength(RewardOffer.MaxAreaLength);
        builder.Property(o => o.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(o => new { o.IsActive, o.CostCredits });
    }
}

internal sealed class RewardRedemptionConfiguration : IEntityTypeConfiguration<RewardRedemption>
{
    public void Configure(EntityTypeBuilder<RewardRedemption> builder)
    {
        builder.ToTable("reward_redemptions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.OfferTitle).HasMaxLength(RewardOffer.MaxTitleLength).IsRequired();
        builder.Property(r => r.PartnerName).HasMaxLength(RewardOffer.MaxPartnerLength).IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(40).IsRequired();
        builder.Property(r => r.VoucherCode).HasMaxLength(RewardRedemption.MaxVoucherLength).IsRequired();
        builder.Property(r => r.Instructions).HasMaxLength(400).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(r => new { r.WorkspaceId, r.CreatedAt });
        builder.HasIndex(r => r.RedeemedByUserId);
        builder.HasIndex(r => r.OfferId);
        builder.HasOne<RelationshipWorkspace>().WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RewardOffer>().WithMany().HasForeignKey(r => r.OfferId).OnDelete(DeleteBehavior.Restrict);

        // No foreign key on RedeemedByUserId: the voucher stays with the couple if the redeeming
        // partner deletes their account; deletion replaces the id with Guid.Empty.
    }
}
