using System.Net;
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
}
