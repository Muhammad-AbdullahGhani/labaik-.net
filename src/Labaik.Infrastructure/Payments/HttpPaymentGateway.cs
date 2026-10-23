using System.Net.Http.Json;
using System.Text.Json;
using Labaik.Application.Payments;

namespace Labaik.Infrastructure.Payments;

public sealed class HttpPaymentGateway(HttpClient client) : IPaymentGateway
{
    public async Task<GatewayLedgerPage> ListAsync(Guid? after, CancellationToken ct)
    {
        using var response = await client.GetAsync("transactions" + (after is null ? "" : $"?after={after:D}"),ct);
        if (!response.IsSuccessStatusCode) throw new PaymentDependencyException();
        return await response.Content.ReadFromJsonAsync<GatewayLedgerPage>(ct) ?? throw new PaymentDependencyException();
    }
    public async Task<GatewaySnapshot> CreateAsync(CheckoutCommand command, CancellationToken ct)
    {
        try { using var response = await client.PostAsJsonAsync("checkouts", command, ct); return await Read(response, ct); }
        catch (Exception error) when (error is HttpRequestException or JsonException) { throw new PaymentDependencyException(); }
    }
    public async Task<GatewaySnapshot> QueryAsync(Guid checkoutId, CancellationToken ct)
    {
        try { using var response = await client.GetAsync($"checkouts/{checkoutId}", ct); return await Read(response, ct); }
        catch (Exception error) when (error is HttpRequestException or JsonException) { throw new PaymentDependencyException(); }
    }
    public async Task<RefundSnapshot> RefundAsync(Guid transactionId, string key, long amountMinor, string currency, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("refunds", new RefundCommand(transactionId, key, amountMinor, currency), ct);
        if (!response.IsSuccessStatusCode) throw new PaymentDependencyException();
        return await response.Content.ReadFromJsonAsync<RefundSnapshot>(ct) ?? throw new PaymentDependencyException();
    }
    private static async Task<GatewaySnapshot> Read(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode) throw new PaymentDependencyException();
        return await response.Content.ReadFromJsonAsync<GatewaySnapshot>(ct) ?? throw new PaymentDependencyException();
    }
}

public sealed class HttpPaymentEventPublisher(HttpClient client) : IPaymentEventPublisher
{
    public async Task PublishAsync(PaymentEvent message, CancellationToken ct)
    {
        if (client.BaseAddress is null || !client.DefaultRequestHeaders.Contains("X-Event-Receiver-Key")) throw new PaymentDependencyException();
        // The receiver must acknowledge durable receipt and use this stable key to deduplicate retries.
        using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = JsonContent.Create(message) };
        request.Headers.Add("Idempotency-Key", message.Id.ToString("N"));
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new PaymentDependencyException();
    }
}
