using Labaik.Application.Payments;
using Labaik.Application.Files;
using Labaik.Infrastructure.Operations;
using Labaik.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Labaik.Infrastructure.Payments;

public static class PaymentHosting
{
    public static IServiceCollection AddPaymentCore(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(new WorkPolicy());
        services.AddSingleton(new BookingPolicy { BaseMinor = config.GetValue("Booking:BaseMinor", 10000L), DailyLimit = config.GetValue("Booking:DailyLimit", 50),
            DateAddons = (config.GetSection("Booking:DateAddons").Get<Dictionary<string,long>>() ?? new()).ToDictionary(x => DateOnly.ParseExact(x.Key, "yyyy-MM-dd"), x => x.Value) });
        services.AddScoped<AssignmentService>(); services.AddScoped<PrivateFileService>();
        services.AddSingleton(new PayoutPolicy { PerformerFeeMinor = config.GetValue("Payouts:PerformerFeeMinor", 7000L), Accounts = config.GetSection("Payouts:Accounts").Get<Dictionary<string,string>>() ?? new() });
        services.AddScoped<CompletionService>(); services.AddScoped<PayoutService>();
        services.AddSingleton<WorkerIdentity>(); services.AddScoped<OperationsScheduler>();
        services.AddSingleton<IPrivateBlobStore>(_ => new AzurePrivateBlobStore(config["Storage:ConnectionString"] ?? throw new FileStoreUnavailableException(), config["Storage:Container"] ?? "private-evidence"));
        services.AddScoped<BookingMoney>(); services.AddScoped<BookingService>();
        services.AddSingleton(_ =>
        {
            try { return new WebhookCodec(Convert.FromBase64String(config["Payments:WebhookEncryptionKey"] ?? ""), Convert.FromBase64String(config["Payments:WebhookSignatureKey"] ?? "")); }
            catch (ArgumentException) { throw new PaymentDependencyException(); }
            catch (FormatException) { throw new PaymentDependencyException(); }
        });
        services.AddHttpClient<IPaymentGateway, HttpPaymentGateway>(client =>
        {
            client.BaseAddress = new Uri(config["Payments:GatewayUrl"] ?? throw new PaymentDependencyException());
            client.DefaultRequestHeaders.Add("X-Gateway-Key", config["Gateway:ApiKey"] ?? throw new PaymentDependencyException());
            client.Timeout = TimeSpan.FromSeconds(3);
        });
        services.AddHttpClient<INotificationTransport, NotificationTransport>(client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<IPaymentEventPublisher, BookingEventPublisher>();
        services.AddScoped<PaymentCheckout>(); services.AddScoped<PaymentIngress>();
        services.AddScoped<WorkQueue>(); services.AddScoped<PaymentPump>();
        // Worker activation is explicit so the migration runner never starts external delivery.
        return services;
    }
}

public sealed class PaymentWorker(IServiceScopeFactory scopes, ILogger<PaymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var nextHeartbeat=DateTimeOffset.MinValue;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if(DateTimeOffset.UtcNow>=nextHeartbeat)
                    {
                        await scope.ServiceProvider.GetRequiredService<OperationsScheduler>().TickAsync(stoppingToken);
                        nextHeartbeat=DateTimeOffset.UtcNow.AddSeconds(5);
                    }
                    var ran = await scope.ServiceProvider.GetRequiredService<PaymentPump>().RunOnceAsync(ct: stoppingToken);
                    if (!ran) await Task.Delay(250, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception) { logger.LogWarning("Payment worker unavailable; durable claims will resume"); await Task.Delay(1000, stoppingToken); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
