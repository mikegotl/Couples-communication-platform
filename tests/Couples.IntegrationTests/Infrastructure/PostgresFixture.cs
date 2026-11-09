using Couples.Application.Abstractions;
using Couples.Infrastructure;
using Couples.Infrastructure.Identity;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Couples.IntegrationTests.Infrastructure;

/// <summary>
/// One disposable PostgreSQL container per test run, migrated with the real EF Core migrations.
/// Tests isolate themselves by creating their own users and workspaces.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => container.GetConnectionString();

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var provider = BuildServices();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CouplesDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    /// <summary>Real infrastructure registrations against the test database with a controllable clock.</summary>
    public ServiceProvider BuildServices(FakeClock? clock = null, ILoggerProvider? logs = null, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            if (logs is not null)
            {
                builder.AddProvider(logs);
            }
        });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddInfrastructure(ConnectionString);
        services.AddSingleton<IClock>(clock ?? Clock);
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async Task<Guid> CreateUserAsync()
    {
        await using var provider = BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        var user = new ApplicationUser();
        var email = $"{user.Id:N}@example.test";
        user.UserName = email;
        user.NormalizedUserName = email.ToUpperInvariant();
        user.Email = email;
        user.NormalizedEmail = email.ToUpperInvariant();
        user.EmailConfirmed = true;
        user.SecurityStamp = Guid.NewGuid().ToString();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
