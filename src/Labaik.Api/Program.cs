using Labaik.Infrastructure.Hosting;
using Labaik.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// Consume the runner switch before ASP.NET's key/value command-line configuration parser sees it.
var migrateOnly = args is ["--migrate"];
if (args.Contains("--migrate") && !migrateOnly)
    throw new InvalidOperationException("Use --migrate alone.");
var builder = WebApplication.CreateBuilder(migrateOnly ? [] : args);
ServiceHost.AddHealthOnlyHost(builder, "app");
builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<Npgsql.NpgsqlDataSource>(), postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "app")));
var app = builder.Build();
// Validate configuration without opening a database connection or applying migrations.
_ = app.Services.GetRequiredService<Npgsql.NpgsqlDataSource>();

// Deployment invokes this separate process with a migrator credential; normal HTTP startup never migrates.
if (migrateOnly)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        app.Logger.LogInformation("App migrations completed");
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
await app.RunAsync();

// Named entry point keeps WebApplicationFactory types unambiguous when testing both executables.
public partial class ApiProgram;
