using Couples.Application;
using Couples.Infrastructure;
using Couples.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration.GetConnectionString("Couples"));

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<PrivacyMaintenanceWorker>();

var host = builder.Build();
await host.RunAsync();
