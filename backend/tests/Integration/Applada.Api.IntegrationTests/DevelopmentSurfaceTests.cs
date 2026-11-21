using System.Net;
using System.Text;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;

namespace Applada.Api.IntegrationTests;

/// <summary>
/// OpenAPI e Scalar existem só em Development. Fora dele as rotas não existem: anônimo recebe 401 do
/// fallback autenticado e autenticado recebe 404, nunca o documento nem a página.
/// </summary>
[Collection("api")]
public class DevelopmentSurfaceTests : IClassFixture<DevelopmentApiFactory>
{
    private static readonly string[] DevOnlyPaths = ["/openapi/v1.json", "/scalar", "/scalar/v1"];

    // Trechos que só aparecem no documento OpenAPI ou na página do Scalar.
    private static readonly string[] DevOnlyMarkers = ["\"openapi\"", "\"paths\"", "@scalar", "api-reference", "createApiReference"];

    private readonly ApiFactory _production;
    private readonly DevelopmentApiFactory _development;

    public DevelopmentSurfaceTests(ApiFactory production, DevelopmentApiFactory development)
    {
        _production = production;
        _development = development;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Development_401_keeps_jwt_error_details_for_debugging()
    {
        using var client = _development.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", TestJwt.Issue("dev-details", audience: "outra-api", now: _development.Clock.GetUtcNow()));

        using var response = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("error_description", Assert.Single(response.Headers.WwwAuthenticate).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_profile_rejects_invalid_json_and_media_type_with_client_errors()
    {
        using var client = _development.CreateClientFor(MatchesApi.NewSubject("dev-profile"));
        var route = new Uri("/api/v1/me/profile", UriKind.Relative);

        using var invalidJson = new StringContent("{\"displayName\":", Encoding.UTF8, "application/json");
        using var invalid = await client.PutAsync(route, invalidJson, Ct);
        using var problem = await ProblemAssertions.AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "validation");
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("displayName", out _));

        using var plainText = new StringContent("João", Encoding.UTF8, "text/plain");
        using var unsupported = await client.PutAsync(route, plainText, Ct);
        using var mediaProblem = await ProblemAssertions.AssertProblemAsync(unsupported, HttpStatusCode.UnsupportedMediaType, "unsupported-media-type");

        using var unknownCharset = new StringContent("{\"displayName\":\"João\"}", Encoding.UTF8, "application/json");
        unknownCharset.Headers.ContentType!.CharSet = "x-applada-unsupported";
        using var charsetResponse = await client.PutAsync(route, unknownCharset, Ct);
        using var charsetProblem = await ProblemAssertions.AssertProblemAsync(charsetResponse, HttpStatusCode.UnsupportedMediaType, "unsupported-media-type");
    }

    [Fact]
    public async Task Production_has_no_openapi_or_scalar()
    {
        using var anonymous = _production.CreateClient();
        using var authenticated = _production.CreateClientFor(MatchesApi.NewSubject("dev-surface"));

        foreach (var path in DevOnlyPaths)
        {
            using var anonymousResponse = await anonymous.GetAsync(new Uri(path, UriKind.Relative), Ct);
            await AssertAbsentAsync(anonymousResponse, path, HttpStatusCode.Unauthorized);

            using var authenticatedResponse = await authenticated.GetAsync(new Uri(path, UriKind.Relative), Ct);
            await AssertAbsentAsync(authenticatedResponse, path, HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Development_serves_openapi_document()
    {
        // Em Development o documento é anônimo (o Scalar precisa dele antes do login); fora de Development não existe.
        using var client = _development.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"/api/v1/matches\"", body, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");

        var bearer = schemes.GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());

        var authorizationCode = schemes.GetProperty("OAuth2").GetProperty("flows").GetProperty("authorizationCode");
        Assert.Equal(
            TestJwt.Issuer + "/protocol/openid-connect/auth",
            authorizationCode.GetProperty("authorizationUrl").GetString());
        Assert.Equal(
            TestJwt.Issuer + "/protocol/openid-connect/token",
            authorizationCode.GetProperty("tokenUrl").GetString());
    }

    [Fact]
    public async Task Development_serves_scalar_page()
    {
        using var client = _development.CreateClient();

        using var response = await client.GetAsync(new Uri("/scalar/v1", UriKind.Relative), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("applada-dev-scalar", body, StringComparison.Ordinal);
    }

    private static async Task AssertAbsentAsync(HttpResponseMessage response, string path, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.True(response.StatusCode == expected, $"{path}: esperado {(int)expected}, veio {(int)response.StatusCode}.");
        foreach (var marker in DevOnlyMarkers)
        {
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
