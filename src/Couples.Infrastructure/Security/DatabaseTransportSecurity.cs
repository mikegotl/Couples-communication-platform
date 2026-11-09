using Npgsql;

namespace Couples.Infrastructure.Security;

/// <summary>
/// Refuses to start against a database connection that isn't encrypted in transit, unless
/// configuration explicitly allows it (local development and tests).
/// </summary>
public static class DatabaseTransportSecurity
{
    public const string RequireTlsKey = "Database:RequireTls";

    public static void Ensure(string? connectionString, bool requireTls)
    {
        if (!requireTls || string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.SslMode is not (SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull))
        {
            throw new InvalidOperationException(
                "The database connection must use TLS (SSL Mode=VerifyFull, VerifyCA or Require). " +
                $"Set {RequireTlsKey}=false only for a local database.");
        }
    }
}
