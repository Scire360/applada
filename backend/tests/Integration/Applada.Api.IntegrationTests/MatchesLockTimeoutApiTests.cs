using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class MatchesLockTimeoutApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Patch_on_locked_match_returns_503_lock_timeout_with_retry_after()
    {
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Matches:LockTimeout", "00:00:01"));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Issue($"lock-{Guid.NewGuid():N}", now: factory.Clock.GetUtcNow()));

        var startsAt = factory.Clock.GetUtcNow().AddDays(2).ToString("O", CultureInfo.InvariantCulture);
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/matches")
        {
            Content = new StringContent(
                $$"""{"title":"Pelada","startsAt":"{{startsAt}}","timeZone":"America/Sao_Paulo","durationMinutes":60,"capacity":10,"visibility":"public"}""",
                Encoding.UTF8,
                "application/json"),
        };
        create.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        using var created = await client.SendAsync(create, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var matchId = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("id").GetGuid();

        // Outra transação segura o lock da partida enquanto o PATCH espera.
        var connectionString = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("applada");
        await using var holder = new NpgsqlConnection(connectionString);
        await holder.OpenAsync(Ct);
        await using var holderTx = await holder.BeginTransactionAsync(Ct);
        await using (var lockCmd = new NpgsqlCommand("SELECT id FROM matches.matches WHERE id = $1 FOR UPDATE", holder, holderTx))
        {
            lockCmd.Parameters.Add(new NpgsqlParameter { Value = matchId });
            await lockCmd.ExecuteScalarAsync(Ct);
        }

        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/matches/{matchId}")
        {
            Content = new StringContent("""{"title":"Travada"}""", Encoding.UTF8, "application/json"),
        };
        patch.Headers.TryAddWithoutValidation("If-Match", "W/\"1\"");
        using var response = await client.SendAsync(patch, Ct);
        await holderTx.RollbackAsync(Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("urn:applada:problem:lock-timeout", problem.RootElement.GetProperty("type").GetString());
    }
}
