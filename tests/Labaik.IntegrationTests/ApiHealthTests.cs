using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Labaik.IntegrationTests;

public sealed class ApiHealthTests
{
    [Theory]
    [InlineData("/health/live", HttpStatusCode.OK)]
    [InlineData("/health/ready", HttpStatusCode.ServiceUnavailable)]
    [InlineData("/bookings", HttpStatusCode.NotFound)]
    public async Task Health_only_service_works_without_a_database(string path, HttpStatusCode expected)
    {
        await using var factory = new WebApplicationFactory<ApiProgram>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Database",
                "Host=127.0.0.1;Port=1;Database=unavailable;Username=unused;Timeout=1;Command Timeout=1;Pooling=false"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Correlation-ID", "untrusted-input");
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        // Caller-controlled content must not be echoed into the correlation/logging field.
        Assert.True(Guid.TryParseExact(response.Headers.GetValues("X-Correlation-ID").Single(), "N", out _));
        if (expected == HttpStatusCode.ServiceUnavailable)
            Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}
