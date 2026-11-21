using System.Net;
using Applada.Api.IntegrationTests.Infrastructure;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class HealthTests
{
    private readonly ApiFactory _factory;

    public HealthTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Live_returns_200()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_returns_200()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
