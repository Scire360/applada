using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Applada.Integration.Tests;

public sealed class HealthEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("applada_test")
        .WithUsername("applada")
        .WithPassword("applada-test")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Live_ReturnsOk()
    {
        await using var factory = CreateFactory(_postgres.GetConnectionString());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_WithPostgresConnection_ReturnsOk()
    {
        await using var factory = CreateFactory(_postgres.GetConnectionString());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_WithUnavailablePostgres_ReturnsServiceUnavailable()
    {
        await using var factory = CreateFactory(
            "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Applada", connectionString);
            });
    }
}
