using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Google.Apis.Http;
using Labaik.Application.Identity;
using Labaik.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Labaik.IntegrationTests;

public sealed class FirebaseAdapterTests
{
    [Fact]
    public async Task Default_production_HTTP_factory_initializes_and_rejects_a_malformed_token()
    {
        using var rsa = RSA.Create(2048);
        var credentials = JsonSerializer.Serialize(new
        {
            type = "service_account", project_id = "test-project", private_key_id = "local-test",
            private_key = rsa.ExportPkcs8PrivateKeyPem(), client_email = "local@test-project.iam.gserviceaccount.com",
            token_uri = "https://oauth2.googleapis.com/token"
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Firebase:ProjectId"] = "test-project", ["Firebase:CredentialsJson"] = credentials }).Build();
        using var verifier = new FirebaseTokenVerifier(config);
        await Assert.ThrowsAsync<InvalidFirebaseTokenException>(() => verifier.VerifyAsync("malformed-token"));
    }
    [Theory]
    [InlineData("valid")]
    [InlineData("wrong-project")]
    [InlineData("expired")]
    [InlineData("just-expired")]
    [InlineData("invalid-signature")]
    [InlineData("revoked")]
    public async Task Real_Admin_SDK_verifies_tokens_and_checks_revocation_without_live_credentials(string mode)
    {
        using var rsa = RSA.Create(2048);
        var certRequest = new CertificateRequest("CN=local-sdk-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var now = DateTimeOffset.UtcNow;
        var factory = new LocalGoogleHttpFactory(cert.ExportCertificatePem(), mode == "revoked", now);
        var credentials = JsonSerializer.Serialize(new
        {
            type = "service_account", project_id = "test-project", private_key_id = "local-test",
            private_key = rsa.ExportPkcs8PrivateKeyPem(), client_email = "local@test-project.iam.gserviceaccount.com",
            token_uri = "https://oauth2.googleapis.com/token"
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Firebase:ProjectId"] = "test-project", ["Firebase:CredentialsJson"] = credentials }).Build();
        using var verifier = new FirebaseTokenVerifier(config, factory);
        var header = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new { alg = "RS256", kid = "local-test" }));
        var payload = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new
        {
            aud = mode == "wrong-project" ? "other-project" : "test-project", iss = "https://securetoken.google.com/test-project",
            sub = "local-user", iat = now.AddMinutes(-20).ToUnixTimeSeconds(), auth_time = now.AddMinutes(-20).ToUnixTimeSeconds(),
            exp = (mode == "expired" ? now.AddMinutes(-10) : mode == "just-expired" ? now.AddSeconds(-1) : now.AddMinutes(30)).ToUnixTimeSeconds()
        }));
        var signingInput = header + "." + payload;
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (mode == "invalid-signature") signature[0] ^= 1;
        var token = signingInput + "." + Base64UrlEncoder.Encode(signature);
        if (mode == "valid")
        {
            var identity = await verifier.VerifyAsync(token);
            Assert.Equal("local-user", identity.Uid); Assert.Equal("test-project", identity.Project);
            Assert.True(factory.RevocationChecked);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidFirebaseTokenException>(() => verifier.VerifyAsync(token));
            Assert.Equal(mode is "revoked" or "just-expired", factory.RevocationChecked);
        }
    }
    // Replace HTTP, not FirebaseAuth: SDK cryptography, claims and revocation code all execute unchanged.
    private sealed class LocalGoogleHttpFactory(string certificate, bool revoked, DateTimeOffset now) : Google.Apis.Http.HttpClientFactory
    {
        public bool RevocationChecked { get; private set; }
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new Handler(request =>
        {
            object body;
            var uri = request.RequestUri!.AbsoluteUri;
            if (uri.Contains("/metadata/x509/")) body = new Dictionary<string, string> { ["local-test"] = certificate };
            else if (uri == "https://oauth2.googleapis.com/token") body = new { access_token = "local-only-access-token", token_type = "Bearer", expires_in = 3600 };
            else if (uri.Contains("accounts:lookup"))
            {
                RevocationChecked = true;
                body = new { users = new[] { new { localId = "local-user", validSince = (revoked ? now : now.AddHours(-1)).ToUnixTimeSeconds().ToString() } } };
            }
            else throw new InvalidOperationException("Unexpected outbound SDK request in isolated test.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        });
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
