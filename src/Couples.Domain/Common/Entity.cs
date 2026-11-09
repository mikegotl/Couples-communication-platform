namespace Couples.Domain.Common;

/// <summary>
/// Base type for persisted domain entities. Every sensitive entity added in later
/// milestones must also declare an owner or workspace scope and a retention rule
/// (see ARCHITECTURE.md, "Privacy boundaries").
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }

    public void Touch(DateTimeOffset now)
    {
        if (CreatedAt == default)
        {
            CreatedAt = now;
        }

        UpdatedAt = now;
    }
}
