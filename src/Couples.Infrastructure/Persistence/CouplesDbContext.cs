using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core context. Entities and migrations are introduced from milestone 02 on.
/// </summary>
public sealed class CouplesDbContext(DbContextOptions<CouplesDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("couples");
        base.OnModelCreating(modelBuilder);
    }
}
