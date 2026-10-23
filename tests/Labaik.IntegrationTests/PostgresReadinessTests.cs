using System.Net;
using DotNet.Testcontainers.Builders;
using Labaik.FakeGateway;
using Labaik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Labaik.IntegrationTests;

public sealed class PostgresReadinessTests
{
    [Fact]
    [Trait("Category", "Docker")]
    public async Task Both_services_are_ready_only_while_real_PostgreSQL_is_reachable()
    {
        var appPassword = Guid.NewGuid().ToString("N");
        var gatewayPassword = Guid.NewGuid().ToString("N");
        var appMigratorPassword = Guid.NewGuid().ToString("N");
        var gatewayMigratorPassword = Guid.NewGuid().ToString("N");
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("labaik").WithUsername("postgres").WithPassword(Guid.NewGuid().ToString("N"))
            .WithEnvironment("APP_DB_PASSWORD", appPassword)
            .WithEnvironment("GATEWAY_DB_PASSWORD", gatewayPassword)
            .WithEnvironment("APP_MIGRATOR_PASSWORD", appMigratorPassword)
            .WithEnvironment("GATEWAY_MIGRATOR_PASSWORD", gatewayMigratorPassword)
            // The string/string overload treats the destination as a directory, even when it looks like a filename.
            .WithResourceMapping(FilePath.Of(Path.Combine(AppContext.BaseDirectory, "postgres-init.sh")),
                FilePath.Of("/docker-entrypoint-initdb.d/001-schemas.sh"))
            .Build();
        // Intentionally fail if Docker is unavailable: CI must never silently skip the database proof.
        await postgres.StartAsync();

        string Connection(string user, string password) => new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Username = user, Password = password, Timeout = 1, CommandTimeout = 1, Pooling = false
        }.ConnectionString;
        var appConnection = Connection("labaik_app", appPassword);
        var gatewayConnection = Connection("labaik_gateway", gatewayPassword);
        await using var apiFactory = new WebApplicationFactory<ApiProgram>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Database", appConnection));
        await using var gatewayFactory = new WebApplicationFactory<GatewayProgram>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Database", gatewayConnection));
        using var api = apiFactory.CreateClient();
        using var gateway = gatewayFactory.CreateClient();

        // Starting either HTTP host must not create the migration ledger (or any business tables).
        await using var admin = new NpgsqlConnection(postgres.GetConnectionString());
        await admin.OpenAsync();
        await using (var tables = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema IN ('app', 'fake_gateway')", admin))
            Assert.Equal(0L, await tables.ExecuteScalarAsync());

        // Same MigrateAsync operation as the explicit CLI runners, with distinct schema-owner credentials.
        await using var appDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Connection("labaik_app_migrator", appMigratorPassword),
                options => options.MigrationsHistoryTable("__EFMigrationsHistory", "app")).Options);
        await using var gatewayDb = new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>()
            .UseNpgsql(Connection("labaik_gateway_migrator", gatewayMigratorPassword),
                options => options.MigrationsHistoryTable("__EFMigrationsHistory", "fake_gateway")).Options);
        await appDb.Database.MigrateAsync();
        await gatewayDb.Database.MigrateAsync();
        await appDb.Database.MigrateAsync();
        await gatewayDb.Database.MigrateAsync();
        Assert.Equal(2, (await appDb.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Single(await gatewayDb.Database.GetAppliedMigrationsAsync());

        foreach (var pair in new[] { (appConnection, "fake_gateway"), (gatewayConnection, "app") })
        {
            await using var runtime = new NpgsqlConnection(pair.Item1);
            await runtime.OpenAsync();
            await using var denied = new NpgsqlCommand("SELECT has_schema_privilege(current_user, $1, 'USAGE')", runtime);
            denied.Parameters.AddWithValue(pair.Item2);
            Assert.Equal(false, await denied.ExecuteScalarAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gateway.GetAsync("/health/ready")).StatusCode);

        await admin.CloseAsync();
        await postgres.StopAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await api.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await gateway.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gateway.GetAsync("/health/live")).StatusCode);
    }
}
