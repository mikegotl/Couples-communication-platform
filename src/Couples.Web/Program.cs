using Couples.AI;
using Couples.Application;
using Couples.Infrastructure;
using Couples.Infrastructure.Identity;
using Couples.Infrastructure.Persistence;
using Couples.Web;
using Couples.Web.Components;
using Couples.Web.Components.Account;
using Couples.Web.Email;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddFeatureManagement();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration.GetConnectionString("Couples"))
    .AddCoaching();

// Identity: individual accounts, confirmed email required, lockout, MFA and passkeys available.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<CurrentUser>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        // Length over composition rules, in line with NIST SP 800-63B.
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<CouplesDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(3));

builder.Services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName);
if (string.Equals(builder.Configuration["Email:Mode"], "Pickup", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, PickupDirectoryEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, UnconfiguredEmailSender>();
}

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CouplesDbContext>().Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

// Liveness: process is up. Does not touch the database.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });

// Readiness: external dependencies (PostgreSQL) are reachable.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(Couples.Infrastructure.DependencyInjection.ReadyTag),
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

await app.RunAsync();

/// <summary>Entry point marker for integration tests (WebApplicationFactory).</summary>
public partial class Program;
