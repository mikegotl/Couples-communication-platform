using Couples.Application;
using Couples.Infrastructure;
using Couples.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration.GetConnectionString("Couples"));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
