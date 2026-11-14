using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Applada.Api.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class AuthenticationTests(ApiFactory factory)
{
    private const string MePath = "/api/v1/me";

    [Fact]
    public async Task No_token_returns_401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(MePath, TestContext.Current.CancellationToken);

        AssertUnauthorizedProblem(response);
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        var now = factory.Clock.GetUtcNow();

        var response = await GetMeWith(TestJwt.Issue("auth-expired", expires: now.AddMinutes(-5), now: now));

        AssertUnauthorizedProblem(response);
    }

    [Fact]
    public async Task Token_without_expiration_returns_401()
    {
        var response = await GetMeWith(TestJwt.Issue("auth-no-exp", withExpiration: false));

        AssertUnauthorizedProblem(response);
    }

    [Fact]
    public async Task Wrong_audience_returns_401()
    {
        var response = await GetMeWith(TestJwt.Issue("auth-audience", audience: "outra-api", now: factory.Clock.GetUtcNow()));

        AssertUnauthorizedProblem(response);
    }

    [Fact]
    public async Task Wrong_issuer_returns_401()
    {
        var response = await GetMeWith(TestJwt.Issue("auth-issuer", issuer: "https://outro-issuer.example", now: factory.Clock.GetUtcNow()));

        AssertUnauthorizedProblem(response);
    }

    [Fact]
    public async Task Token_signed_with_other_key_returns_401()
    {
        using var rsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(rsa) { KeyId = TestJwt.SigningKey.KeyId }; // mesmo kid, chave diferente

        var response = await GetMeWith(TestJwt.Issue("auth-other-key", key: otherKey, now: factory.Clock.GetUtcNow()));

        AssertUnauthorizedProblem(response);
    }

    [Fact]
    public async Task Valid_token_returns_200()
    {
        using var client = factory.CreateClientFor("auth-valid");

        var response = await client.GetAsync(MePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetMeWith(string token)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync(MePath, TestContext.Current.CancellationToken);
    }

    private static void AssertUnauthorizedProblem(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
