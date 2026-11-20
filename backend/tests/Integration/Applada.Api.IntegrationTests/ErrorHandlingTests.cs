using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Applada.Api.IntegrationTests.Infrastructure;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class ErrorHandlingTests(ApiFactory factory)
{
    [Fact]
    public async Task Factory_starts_containers_and_api_is_live()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_json_404()
    {
        using var client = factory.CreateClientFor("user-1");

        var response = await client.GetAsync("/rota-que-nao-existe", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("urn:applada:problem:not-found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unhandled_exception_returns_problem_json_without_details()
    {
        using var client = factory.CreateClientFor("user-1");

        var response = await client.GetAsync("/test/throw", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("urn:applada:problem:internal", body, StringComparison.Ordinal);
        Assert.DoesNotContain("segredo-interno", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Domain_problem_keeps_its_specific_type_and_detail()
    {
        using var client = factory.CreateClientFor("user-1");

        var response = await client.GetAsync("/test/problem", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("urn:applada:problem:registration-closed", body, StringComparison.Ordinal);
        Assert.Contains("detalhe-de-dominio", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Jwt_bearer_uses_static_configuration_without_oidc_discovery()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.IsType<StaticConfigurationManager<OpenIdConnectConfiguration>>(options.ConfigurationManager);
    }

    [Fact]
    public async Task Client_token_stays_valid_after_factory_clock_advances()
    {
        var step = TimeSpan.FromDays(30);
        factory.Clock.Advance(step);
        try
        {
            using var client = factory.CreateClientFor("user-1");

            var response = await client.GetAsync("/test/auth", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            factory.Clock.Advance(-step);
        }
    }

    [Fact]
    public void Issued_token_lifetime_brackets_the_given_clock_and_allows_already_expired()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var valid = new JwtSecurityTokenHandler().ReadJwtToken(TestJwt.Issue("u", now: now));
        var expired = new JwtSecurityTokenHandler().ReadJwtToken(TestJwt.Issue("u", expires: now.AddMinutes(-5), now: now));

        Assert.True(valid.ValidFrom <= now.UtcDateTime && now.UtcDateTime < valid.ValidTo);
        Assert.True(expired.ValidFrom < expired.ValidTo && expired.ValidTo <= now.UtcDateTime);
    }
}
