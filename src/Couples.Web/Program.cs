using Couples.AI;
using Couples.Application;
using Couples.Infrastructure;
using Couples.Web.Components;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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

var app = builder.Build();

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

await app.RunAsync();

/// <summary>Entry point marker for integration tests (WebApplicationFactory).</summary>
public partial class Program;
