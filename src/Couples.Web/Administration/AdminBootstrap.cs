using Couples.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Couples.Web.Administration;

/// <summary>
/// Grants the admin role to the accounts listed in <c>Administration:AdminEmails</c>. Only
/// confirmed accounts are promoted, and nothing is removed, so the list is safe to re-run. There
/// is no self-service way to become an admin.
/// </summary>
internal static partial class AdminBootstrap
{
    public const string AdminRole = "Admin";

    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var emails = configuration.GetSection("Administration:AdminEmails").Get<string[]>() ?? [];
        if (emails.Length == 0)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roles.RoleExistsAsync(AdminRole))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(AdminRole));
        }

        foreach (var email in emails.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            var user = await users.FindByEmailAsync(email.Trim());
            if (user is null || !user.EmailConfirmed || await users.IsInRoleAsync(user, AdminRole))
            {
                continue;
            }

            await users.AddToRoleAsync(user, AdminRole);
            LogPromoted(logger, user.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Granted the admin role to user {UserId} from configuration")]
    private static partial void LogPromoted(ILogger logger, Guid userId);
}
