using Couples.Domain.Auditing;
using Couples.Domain.Consent;
using Couples.Domain.Messaging;
using Couples.Domain.Privacy;
using Couples.Domain.Reflections;
using Couples.Domain.Workspaces;
using Couples.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couples.Infrastructure.Persistence.Configurations;

internal sealed class PrivateReflectionConfiguration : IEntityTypeConfiguration<PrivateReflection>
{
    public void Configure(EntityTypeBuilder<PrivateReflection> builder)
    {
        builder.ToTable("private_reflections");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Title).HasMaxLength(PrivateReflection.MaxTitleLength).IsRequired();
        builder.Property(r => r.Body).HasMaxLength(PrivateReflection.MaxBodyLength).IsRequired();
        builder.HasIndex(r => r.OwnerUserId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DraftMessageConfiguration : IEntityTypeConfiguration<DraftMessage>
{
    public void Configure(EntityTypeBuilder<DraftMessage> builder)
    {
        builder.ToTable("draft_messages");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Body).HasMaxLength(DraftMessage.MaxBodyLength).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(d => d.Fingerprint);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasIndex(d => d.OwnerUserId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RelationshipWorkspace>().WithMany().HasForeignKey(d => d.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SharedMessageConfiguration : IEntityTypeConfiguration<SharedMessage>
{
    public void Configure(EntityTypeBuilder<SharedMessage> builder)
    {
        builder.ToTable("shared_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Body).HasMaxLength(DraftMessage.MaxBodyLength).IsRequired();
        builder.Ignore(m => m.IsRevoked);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasIndex(m => new { m.RecipientUserId, m.SharedAt });
        builder.HasIndex(m => new { m.SenderUserId, m.SharedAt });

        // A draft can be shared at most once, even if two requests race.
        builder.HasIndex(m => m.SourceDraftId).IsUnique();

        builder.HasOne<RelationshipWorkspace>().WithMany().HasForeignKey(m => m.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.SenderUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DraftMessage>().WithMany().HasForeignKey(m => m.SourceDraftId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("consent_records");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Purpose).HasConversion<string>().HasMaxLength(40);
        builder.Property(c => c.ContentFingerprint).HasMaxLength(64);
        builder.HasIndex(c => new { c.UserId, c.Purpose, c.SubjectId });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        // No foreign keys: audit history must survive account deletion, holding only random ids.
        builder.ToTable("audit_events");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(60).IsRequired();
        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => a.ActorUserId);
    }
}

internal sealed class DataDeletionRequestConfiguration : IEntityTypeConfiguration<DataDeletionRequest>
{
    public void Configure(EntityTypeBuilder<DataDeletionRequest> builder)
    {
        // No foreign key: the request outlives the account it erased.
        builder.ToTable("data_deletion_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(r => r.Status);
    }
}
