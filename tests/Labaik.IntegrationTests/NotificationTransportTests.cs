using System.Net;
using System.Text.Json;
using Labaik.Application.Payments;
using Labaik.Infrastructure.Operations;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Labaik.IntegrationTests;

public sealed class NotificationTransportTests
{
    private sealed class MailHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public readonly List<(string Key, string Body)> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("https://api.resend.com/emails", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Requests.Add((Assert.Single(request.Headers.GetValues("Idempotency-Key")), await request.Content!.ReadAsStringAsync(ct)));
            return new(status);
        }
    }
    private static IConfiguration Settings() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    { ["Notifications:EmailApiKey"]="synthetic-provider-key", ["Notifications:EmailFrom"]="sender@example.invalid" }).Build();
    [Fact]
    public async Task Email_retries_keep_provider_key_payload_and_localized_content_identical()
    {
        using var handler=new MailHandler(HttpStatusCode.OK);using var http=new HttpClient(handler);using var transport=new NotificationTransport(http,Settings());
        var id=Guid.NewGuid();var text=NotificationTemplates.Render("BookingPaid","ar");
        await transport.EmailAsync("recipient@example.invalid",text,id,default);await transport.EmailAsync("recipient@example.invalid",text,id,default);
        Assert.Equal(2,handler.Requests.Count);Assert.Equal(handler.Requests[0],handler.Requests[1]);Assert.Equal(id.ToString("N"),handler.Requests[0].Key);
        using var body=JsonDocument.Parse(handler.Requests[0].Body);Assert.Equal(text.Body,body.RootElement.GetProperty("text").GetString());
        Assert.Equal("recipient@example.invalid",body.RootElement.GetProperty("to")[0].GetString());
    }
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Provider_failure_is_not_reported_as_delivery_success(HttpStatusCode status)
    {
        using var handler=new MailHandler(status);using var http=new HttpClient(handler);using var transport=new NotificationTransport(http,Settings());
        await Assert.ThrowsAsync<PaymentDependencyException>(()=>transport.EmailAsync("recipient@example.invalid",NotificationTemplates.Render("BookingPaid","en"),Guid.NewGuid(),default));
    }
}
