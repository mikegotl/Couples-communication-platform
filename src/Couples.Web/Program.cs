using Couples.AI;
using Couples.Application;
using Couples.Infrastructure;
using Couples.Infrastructure.Identity;
using Couples.Infrastructure.Persistence;
using Couples.Web;
using Couples.Web.Components;
using Couples.Web.Components.Account;
using Couples.Web.Email;
using Couples.Web.Observability;
using Couples.Web.Security;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddWebSecurity();
builder.AddTelemetry("bridgetogether-web");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = false);
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 64 * 1024);

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
builder.Services.AddOptions<Couples.Web.Safety.SafetyOptions>()
    .BindConfiguration(Couples.Web.Safety.SafetyOptions.SectionName)
    .Validate(o => o.ExitUrl.Scheme == Uri.UriSchemeHttps, "Safety:ExitUrl must be https.")
    .Validate(o => o.Resources.All(r => !string.IsNullOrWhiteSpace(r.Name) && (r.Url is null || r.Url.Scheme == Uri.UriSchemeHttps)), "Safety:Resources need a name, and links must be https.")
    .ValidateOnStart();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "bt-auth" : "__Host-bt-auth";
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
    .AddRoles<IdentityRole<Guid>>()
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

await Couples.Web.Administration.AdminBootstrap.RunAsync(app.Services, app.Configuration, app.Logger);

// First, so HTTPS redirection, HSTS, cookies and rate limits see the real scheme and client.
if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseSecurityHeaders(upgradeInsecure: !app.Environment.IsDevelopment());
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UsePilotGate();
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
