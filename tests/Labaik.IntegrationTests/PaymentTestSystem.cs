using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Labaik.Application.Payments;
using Labaik.Domain.Bookings;
using Labaik.Domain.Pricing;
using Labaik.Domain.Users;
using Labaik.FakeGateway;
using Labaik.Infrastructure.Payments;
using Labaik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Labaik.IntegrationTests;

public sealed class PaymentDatabase : IAsyncLifetime
{
    private readonly string _app = Guid.NewGuid().ToString("N"), _gateway = Guid.NewGuid().ToString("N"),
        _appMigrator = Guid.NewGuid().ToString("N"), _gatewayMigrator = Guid.NewGuid().ToString("N");
    private PostgreSqlContainer _postgres = null!;
    private string Connection(string? user = null, string? password = null) => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        { Username = user ?? "postgres", Password = password ?? _root, MaxPoolSize = 24 }.ConnectionString;
    public byte[] EncryptionKey { get; } = RandomNumberGenerator.GetBytes(32);
    public byte[] SignatureKey { get; } = RandomNumberGenerator.GetBytes(32);
    public string ApiKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string _root = Guid.NewGuid().ToString("N");
    public string AppConnection => Connection("labaik_app", _app);
    public string GatewayConnection => Connection("labaik_gateway", _gateway);
    public AppDbContext App(bool admin = false) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(admin ? Connection() : AppConnection, p => p.MigrationsHistoryTable("__EFMigrationsHistory", "app")).Options);
    public GatewayDbContext Gateway() => new(new DbContextOptionsBuilder<GatewayDbContext>()
        .UseNpgsql(GatewayConnection, p => p.MigrationsHistoryTable("__EFMigrationsHistory", "fake_gateway")).Options);
    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("labaik").WithUsername("postgres").WithPassword(_root)
            .WithEnvironment("APP_DB_PASSWORD", _app).WithEnvironment("GATEWAY_DB_PASSWORD", _gateway)
            .WithEnvironment("APP_MIGRATOR_PASSWORD", _appMigrator).WithEnvironment("GATEWAY_MIGRATOR_PASSWORD", _gatewayMigrator)
            .WithResourceMapping(FilePath.Of(Path.Combine(AppContext.BaseDirectory, "postgres-init.sh")), FilePath.Of("/docker-entrypoint-initdb.d/001-schemas.sh")).Build();
        await _postgres.StartAsync();
        await using var app = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Connection("labaik_app_migrator", _appMigrator), p => p.MigrationsHistoryTable("__EFMigrationsHistory", "app")).Options);
        await using var gateway = new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>()
            .UseNpgsql(Connection("labaik_gateway_migrator", _gatewayMigrator), p => p.MigrationsHistoryTable("__EFMigrationsHistory", "fake_gateway")).Options);
        await app.Database.MigrateAsync(); await gateway.Database.MigrateAsync();
        await using var admin = App(true);
        // Test-only receiver storage models a separate consumer with durable deduplication; not production schema.
        await admin.Database.ExecuteSqlRawAsync("CREATE SCHEMA test_support; CREATE TABLE test_support.receipts (id uuid PRIMARY KEY, fingerprint text NOT NULL)");
    }
    public async Task ResetAsync()
    {
        await using var admin = App(true);
        await admin.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS reject_payment_outbox ON app.\"OutboxMessages\"; DROP FUNCTION IF EXISTS test_support.reject_outbox(); TRUNCATE app.\"DailyCapacities\",app.\"JobRuns\",app.\"OutboxMessages\",app.\"CapturedPayments\",app.\"PaymentAttempts\",app.\"WebhookInbox\",app.\"Bookings\",app.\"Users\",fake_gateway.\"Events\",fake_gateway.\"Charges\",fake_gateway.\"Checkouts\",test_support.receipts CASCADE");
    }
    public async Task<(Guid BookingId, Guid OwnerId)> SeedAsync()
    {
        var user = new User(Guid.NewGuid(), "payment-tests", Guid.NewGuid().ToString("N"), ActorRole.Customer, DateTimeOffset.UtcNow);
        var booking = new Booking(Guid.NewGuid(), user.Id, new DateOnly(2030, 1, 1), new PriceQuote(new Money(1000), default, default), DateTimeOffset.UtcNow);
        await using var db = App(); db.AddRange(user, booking); await db.SaveChangesAsync(); return (booking.Id, user.Id);
    }
    public async Task DueAsync(WorkKind kind)
    {
        await using var db = App(); var name = kind.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.\"JobRuns\" SET \"AvailableAt\"=clock_timestamp()-interval '1 second' WHERE \"Status\"='Pending' AND \"Kind\"={name}");
    }
    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}

public sealed class PaymentTestSystem : IAsyncDisposable
{
    private readonly PaymentDatabase _database;
    private readonly WebApplicationFactory<GatewayProgram> _gateway;
    private readonly TestServer _ingress, _receiver;
    private readonly IHost _ingressHost, _receiverHost;
    public HttpClient GatewayClient { get; }
    public HttpClient Webhooks { get; }
    public WebhookCodec Codec { get; }
    public GatewaySimulation Faults => _gateway.Services.GetRequiredService<GatewaySimulation>();
    public ServiceProvider Services { get; }
    public int DeliveryCalls;
    public bool RejectPublication;
    public Func<PaymentEvent, Task>? AfterPublish;
    public PaymentTestSystem(PaymentDatabase database, ResponseBarrier? barrier = null, Func<IPaymentGateway, IPaymentGateway>? decorate = null)
    {
        _database = database;
        var encryption = database.EncryptionKey; var signature = database.SignatureKey;
        Codec = new(encryption, signature);
        var apiKey = database.ApiKey;
        _gateway = new WebApplicationFactory<GatewayProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", database.GatewayConnection);
            builder.UseSetting("Gateway:ApiKey", apiKey);
            builder.UseSetting("Gateway:ReturnUrl", "https://core.example.invalid/payments/return");
            builder.UseSetting("Payments:WebhookEncryptionKey", Convert.ToBase64String(encryption));
            builder.UseSetting("Payments:WebhookSignatureKey", Convert.ToBase64String(signature));
        });
        GatewayClient = barrier is null ? _gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }) : _gateway.CreateDefaultClient(barrier); GatewayClient.DefaultRequestHeaders.Add("X-Gateway-Key", apiKey);
        GatewayClient.Timeout = TimeSpan.FromSeconds(5);
        _receiverHost = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().Configure(app => app.Run(async context =>
        {
            if (context.Request.Headers["X-Event-Receiver-Key"] != database.ApiKey) { context.Response.StatusCode = 401; return; }
            if (RejectPublication) { context.Response.StatusCode = 503; return; }
            var message = (await context.Request.ReadFromJsonAsync<PaymentEvent>())!;
            if (context.Request.Headers["Idempotency-Key"] != message.Id.ToString("N")) { context.Response.StatusCode = 400; return; }
            var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(message)));
            await using var db = database.App(true);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO test_support.receipts (id,fingerprint) VALUES ({message.Id},{hash}) ON CONFLICT (id) DO NOTHING");
            Interlocked.Increment(ref DeliveryCalls); // Test metric only; durable correctness is in the receipt PK.
            if (AfterPublish is not null) await AfterPublish(message);
            context.Response.StatusCode = 200;
        }))).Start();
        _receiver = _receiverHost.GetTestServer();
        var receiverClient = _receiver.CreateClient();
        receiverClient.DefaultRequestHeaders.Add("X-Event-Receiver-Key", database.ApiKey);
        var services = new ServiceCollection();
        services.AddLogging(); services.AddDbContext<AppDbContext>(options => options.UseNpgsql(database.AppConnection));
        services.AddSingleton(Codec); services.AddSingleton(new WorkPolicy { MaxAttempts = 2 });
        IPaymentGateway gatewayClient = new HttpPaymentGateway(GatewayClient);
        services.AddSingleton(decorate?.Invoke(gatewayClient) ?? gatewayClient);
        services.AddSingleton<IPaymentEventPublisher>(new HttpPaymentEventPublisher(receiverClient));
        services.AddSingleton(new BookingPolicy()); services.AddScoped<BookingMoney>(); services.AddScoped<BookingService>();
        services.AddScoped<PaymentCheckout>(); services.AddScoped<PaymentIngress>(); services.AddScoped<WorkQueue>(); services.AddScoped<PaymentPump>();
        Services = services.BuildServiceProvider();
        _ingressHost = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().Configure(app => app.Run(async context =>
        {
            if (context.Request.Path != "/webhooks/gateway") { context.Response.StatusCode = 404; return; }
            await using var scope = Services.CreateAsyncScope(); context.RequestServices = scope.ServiceProvider;
            await PaymentWebhookEndpoint.HandleAsync(context);
        }))).Start();
        _ingress = _ingressHost.GetTestServer();
        Webhooks = _ingress.CreateClient();
        Webhooks.BaseAddress = new Uri("http://localhost/webhooks/gateway");
    }
    public async Task<CheckoutAttemptView> Checkout(Guid booking, Guid owner, string key)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentCheckout>().CheckoutAsync(booking, owner, key);
    }
    public async Task<bool> Pump(WorkKind kind)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentPump>().RunOnceAsync(kind);
    }
    public async Task Drain(WorkKind kind)
    {
        for (var i = 0; i < 100; i++) if (!await Pump(kind)) return;
        throw new InvalidOperationException("Unexpected infinite drain.");
    }
    public async Task<HttpResponseMessage> Send(byte[] body, string? signature = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/gateway") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Gateway-Signature", signature ?? Codec.Sign(body)); return await Webhooks.SendAsync(request);
    }
    public async Task<GatewaySnapshot> Pay(Guid checkoutId, string outcome = "Succeeded")
    {
        using var result = await GatewayClient.PostAsJsonAsync($"/checkouts/{checkoutId}/pay", new SimulatedPayment(outcome));
        result.EnsureSuccessStatusCode(); return (await result.Content.ReadFromJsonAsync<GatewaySnapshot>())!;
    }
    public async Task<byte[]> Event(Guid checkoutId, string status)
    {
        await using var db = _database.Gateway(); var rows = await db.Events.Where(x => x.CheckoutId == checkoutId).ToListAsync();
        return rows.Select(x => x.Body).First(body => Codec.VerifyAndDecrypt(body, Codec.Sign(body)).Status == status);
    }
    public async Task<int> ReceiptCount()
    { await using var db = _database.App(true); return await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM test_support.receipts").SingleAsync(); }
    public async ValueTask DisposeAsync()
    {
        Webhooks.Dispose(); GatewayClient.Dispose(); _ingressHost.Dispose(); _receiverHost.Dispose();
        await Services.DisposeAsync(); await _gateway.DisposeAsync();
    }
}

public sealed class ResponseBarrier : DelegatingHandler
{
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/checkouts")
        { Reached.TrySetResult(); await Release.Task.WaitAsync(ct); }
        return response;
    }
}
public sealed class SnapshotOverride(IPaymentGateway inner, Func<GatewaySnapshot, GatewaySnapshot> change) : IPaymentGateway
{
    public Task<GatewaySnapshot> CreateAsync(CheckoutCommand command, CancellationToken ct) => inner.CreateAsync(command, ct);
    public async Task<GatewaySnapshot> QueryAsync(Guid id, CancellationToken ct) => change(await inner.QueryAsync(id, ct));
}

public sealed class QueryBarrier(IPaymentGateway inner) : IPaymentGateway
{
    private int _queries;
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<GatewaySnapshot> CreateAsync(CheckoutCommand command, CancellationToken ct) => inner.CreateAsync(command, ct);
    public async Task<GatewaySnapshot> QueryAsync(Guid id, CancellationToken ct)
    {
        var snapshot = await inner.QueryAsync(id, ct);
        if (Interlocked.Increment(ref _queries) == 1) { Reached.TrySetResult(); await Release.Task.WaitAsync(ct); }
        return snapshot;
    }
}
