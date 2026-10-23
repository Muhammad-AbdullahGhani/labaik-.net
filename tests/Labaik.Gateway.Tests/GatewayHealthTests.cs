using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Labaik.Gateway.Tests;

public sealed class GatewayHealthTests
{
    [Theory]
    [InlineData("/health/live", HttpStatusCode.OK)]
    [InlineData("/health/ready", HttpStatusCode.ServiceUnavailable)]
    [InlineData("/checkout", HttpStatusCode.NotFound)]
    public async Task Gateway_exposes_health_only(string path, HttpStatusCode expected)
    {
        await using var factory = new WebApplicationFactory<GatewayProgram>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Database",
                "Host=127.0.0.1;Port=1;Database=unavailable;Username=unused;Timeout=1;Command Timeout=1;Pooling=false"));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.True(Guid.TryParseExact(response.Headers.GetValues("X-Correlation-ID").Single(), "N", out _));
    }
}
