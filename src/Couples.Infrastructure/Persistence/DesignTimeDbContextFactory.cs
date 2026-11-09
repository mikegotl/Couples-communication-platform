using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Builds the context through the same registrations the app
/// uses, so migrations match the runtime model exactly. Reads ConnectionStrings__Couples from
/// the environment and otherwise falls back to the local Docker Compose database.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CouplesDbContext>
{
    private const string LocalDevelopment =
        "Host=localhost;Port=5432;Database=couples;Username=couples;Password=couples_local_dev_only";

    public CouplesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Couples") ?? LocalDevelopment;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddInfrastructure(connectionString);
        return services.BuildServiceProvider().GetRequiredService<CouplesDbContext>();
    }
}
