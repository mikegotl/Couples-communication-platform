using Couples.Application;
using Couples.Infrastructure;
using Couples.Worker;

var builder = Host.CreateApplicationBuilder(args);

Couples.Infrastructure.Security.DatabaseTransportSecurity.Ensure(
    builder.Configuration.GetConnectionString("Couples"),
    builder.Configuration.GetValue(Couples.Infrastructure.Security.DatabaseTransportSecurity.RequireTlsKey, true));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration.GetConnectionString("Couples"));

builder.AddTelemetry();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<PrivacyMaintenanceWorker>();

var host = builder.Build();
await host.RunAsync();
