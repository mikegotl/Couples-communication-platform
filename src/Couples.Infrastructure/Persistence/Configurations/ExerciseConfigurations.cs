using System.Text.Json;
using Couples.Domain.Exercises;
using Couples.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couples.Infrastructure.Persistence.Configurations;

internal sealed class ExerciseDefinitionConfiguration : IEntityTypeConfiguration<ExerciseDefinition>
{
    private static readonly JsonSerializerOptions StepJson = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ExerciseDefinition> builder)
    {
        builder.ToTable("exercise_definitions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Key).HasMaxLength(ExerciseDefinition.MaxKeyLength).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(120).IsRequired();
        builder.Property(d => d.Summary).HasMaxLength(600).IsRequired();
        builder.Property(d => d.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(d => d.ReviewStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.ReviewedBy).HasMaxLength(ExerciseDefinition.MaxReviewerLength);
        builder.Property(d => d.ReviewNotes).HasMaxLength(ExerciseDefinition.MaxReviewNotesLength);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasIndex(d => new { d.Key, d.Version }).IsUnique();

        // Steps are immutable content for this version, stored as one JSON document.
        builder.Property(d => d.Steps)
            .HasColumnType("jsonb")
            .HasConversion(
                steps => JsonSerializer.Serialize(steps, StepJson),
                json => JsonSerializer.Deserialize<List<ExerciseStep>>(json, StepJson) ?? new List<ExerciseStep>(),
                new ValueComparer<IReadOnlyList<ExerciseStep>>(
                    (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b)),
                    steps => steps.Count,
                    steps => steps));
    }
}

internal sealed class ExerciseAttemptConfiguration : IEntityTypeConfiguration<ExerciseAttempt>
{
    public void Configure(EntityTypeBuilder<ExerciseAttempt> builder)
    {
        builder.ToTable("exercise_attempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasIndex(a => new { a.OwnerUserId, a.Status });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ExerciseDefinition>().WithMany().HasForeignKey(a => a.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(a => a.Responses).WithOne().HasForeignKey(r => r.AttemptId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Responses).HasField("responses").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ExerciseResponseConfiguration : IEntityTypeConfiguration<ExerciseResponse>
{
    public void Configure(EntityTypeBuilder<ExerciseResponse> builder)
    {
        builder.ToTable("exercise_responses");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.StepKey).HasMaxLength(60).IsRequired();
        builder.Property(r => r.Value).HasMaxLength(ExerciseStep.DefaultMaxLength).IsRequired();
        builder.HasIndex(r => new { r.AttemptId, r.StepKey }).IsUnique();
        builder.HasIndex(r => r.OwnerUserId);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
