using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests;

/// <summary>
/// Hosts the web app in-memory. Uses the Production environment so tests see the same
/// defaults as a deployed app, and a connection string pointing at an unreachable port
/// so no test silently depends on a local database.
/// </summary>
public sealed class CouplesWebFactory : WebApplicationFactory<Program>
{
    public CouplesWebFactory()
    {
        // Production cookies are Secure-only, so talk to the app over https like a browser would.
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Database:RequireTls", "false");
        builder.UseSetting("DataProtection:AllowUnprotectedKeys", "true");
        builder.UseSetting("ConnectionStrings:Couples", "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1");

        // The real key ring lives in the database, which these tests deliberately can't reach.
        builder.ConfigureTestServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
    }
}
