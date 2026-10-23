using Labaik.FakeGateway;
using Labaik.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;

// Consume the runner switch before ASP.NET's key/value command-line configuration parser sees it.
var migrateOnly = args is ["--migrate"];
if (args.Contains("--migrate") && !migrateOnly)
    throw new InvalidOperationException("Use --migrate alone.");
var builder = WebApplication.CreateBuilder(migrateOnly ? [] : args);
ServiceHost.AddHealthOnlyHost(builder, "fake_gateway");
builder.Services.AddDbContext<GatewayDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<Npgsql.NpgsqlDataSource>(), postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "fake_gateway")));
GatewayEndpoints.Add(builder);
var app = builder.Build();
// Validate configuration without opening a database connection or applying migrations.
_ = app.Services.GetRequiredService<Npgsql.NpgsqlDataSource>();

if (migrateOnly)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GatewayDbContext>().Database.MigrateAsync();
        app.Logger.LogInformation("Gateway migrations completed");
    }
    catch (Exception exception)
    {
        // A failed migration must stop deployment, without printing connection/provider details.
        app.Logger.LogError("Migration failed ({ErrorType}); deployment must stop", exception.GetType().Name);
        Environment.ExitCode = 1;
    }
    await app.DisposeAsync();
    return;
}

ServiceHost.MapHealthOnlyHost(app);
GatewayEndpoints.Map(app);
await app.RunAsync();

public partial class GatewayProgram;
