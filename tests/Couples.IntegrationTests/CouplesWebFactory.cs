using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Couples.IntegrationTests;

/// <summary>
/// Hosts the web app in-memory. Uses the Production environment so tests see the same
/// defaults as a deployed app, and a connection string pointing at an unreachable port
/// so no test silently depends on a local database.
/// </summary>
public sealed class CouplesWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Couples", "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1");
    }
}
