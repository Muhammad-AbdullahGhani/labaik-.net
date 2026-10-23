using Labaik.Application.Payments;
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
        services.AddSingleton(_ => new WebhookCodec(Convert.FromBase64String(config["Payments:WebhookEncryptionKey"] ?? ""),
            Convert.FromBase64String(config["Payments:WebhookSignatureKey"] ?? "")));
        services.AddHttpClient<IPaymentGateway, HttpPaymentGateway>(client =>
        {
            client.BaseAddress = new Uri(config["Payments:GatewayUrl"] ?? throw new PaymentDependencyException());
            client.DefaultRequestHeaders.Add("X-Gateway-Key", config["Gateway:ApiKey"] ?? throw new PaymentDependencyException());
            client.Timeout = TimeSpan.FromSeconds(3);
        });
        services.AddHttpClient<IPaymentEventPublisher, HttpPaymentEventPublisher>(client =>
        {
            if (config["Payments:EventReceiverUrl"] is { Length: > 0 } url) client.BaseAddress = new Uri(url);
            if (config["Payments:EventReceiverKey"] is { Length: > 0 } key) client.DefaultRequestHeaders.Add("X-Event-Receiver-Key", key);
            client.Timeout = TimeSpan.FromSeconds(3);
        });
        services.AddScoped<PaymentCheckout>(); services.AddScoped<PaymentIngress>();
        services.AddScoped<WorkQueue>(); services.AddScoped<PaymentPump>();
        // Deliberately do NOT map checkout/callback routes or start PaymentWorker until step 4's capacity/refunds exist.
        return services;
    }
}

public sealed class PaymentWorker(IServiceScopeFactory scopes, ILogger<PaymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
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
