using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Applada.Api.IntegrationTests;

/// <summary>
/// Guardas das escritas de Matches observadas por log: saída antecipada de quem não organiza e a falha de divergência
/// entre contadores e inscrições. Cada teste usa um host derivado com captura de log.
/// </summary>
[Collection("api")]
public class MatchesGuardTests(ApiFactory factory)
{
    private const string MatchesPath = "/api/v1/matches";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Patch_capacity_increase_by_non_organizer_returns_403_without_reading_registrations()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = HostWithLogs(logs);
        using var organizer = ClientFor(host);
        using var intruder = ClientFor(host);
        var matchId = await CreateMatchAsync(organizer, capacity: 1);
        await SeedRegistrationsAsync(host, matchId, [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()]);
        await GetUserIdAsync(intruder); // provisiona antes da medição
        logs.Clear();

        using var response = await PatchAsync(intruder, matchId, "W/\"1\"", """{ "capacity": 3 }""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("urn:applada:problem:not-organizer", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var sql = logs.Entries.Where(e => e.Category == "Microsoft.EntityFrameworkCore.Database.Command").Select(e => e.Message).ToArray();
        Assert.Contains(sql, s => s.Contains("FOR UPDATE", StringComparison.Ordinal));
        Assert.DoesNotContain(sql, s => s.Contains("matches.registrations", StringComparison.Ordinal));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var match = await db.Matches.AsNoTracking().SingleAsync(m => m.Id == matchId, Ct);
        Assert.Equal((1, 1, 2, 1), (match.Capacity, match.ConfirmedCount, match.WaitlistCount, match.Version));
        Assert.Equal(0, await db.OutboxMessages.CountAsync(o => o.AggregateId == matchId && o.EventType == "matches.spot-confirmed.v1", Ct));
    }

    [Fact]
    public async Task Cancel_by_non_organizer_returns_403_and_keeps_match_scheduled()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = HostWithLogs(logs);
        using var organizer = ClientFor(host);
        using var intruder = ClientFor(host);
        var matchId = await CreateMatchAsync(organizer, capacity: 5);

        using var response = await intruder.PostAsync($"{MatchesPath}/{matchId}/cancellation", content: null, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var match = await db.Matches.AsNoTracking().SingleAsync(m => m.Id == matchId, Ct);
        Assert.Equal((MatchStatus.Scheduled, 1), (match.Status, match.Version));
        Assert.Equal(0, await db.OutboxMessages.CountAsync(o => o.AggregateId == matchId && o.EventType == "matches.match-cancelled.v1", Ct));
    }

    [Fact]
    public async Task Counter_divergence_on_patch_returns_500_and_logs_match_id()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = HostWithLogs(logs);
        using var organizer = ClientFor(host);
        var matchId = await CreateMatchAsync(organizer, capacity: 1);

        // O contador diz que há 2 em espera, mas não existe nenhuma linha de inscrição: estado divergente.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
            await db.Database.ExecuteSqlAsync($"UPDATE matches.matches SET waitlist_count = 2 WHERE id = {matchId}", Ct);
        }

        logs.Clear();

        using var response = await PatchAsync(organizer, matchId, "W/\"1\"", """{ "capacity": 3 }""");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var divergence = Assert.Single(logs.Entries, e => e.Category == "Applada.Modules.Matches.Api.MatchEndpoints");
        Assert.Equal(LogLevel.Error, divergence.Level);
        Assert.Contains(matchId.ToString(), divergence.Message, StringComparison.Ordinal);
        Assert.IsType<MatchStateDivergenceException>(divergence.Exception);

        await using var verify = host.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var match = await verifyDb.Matches.AsNoTracking().SingleAsync(m => m.Id == matchId, Ct);
        Assert.Equal((1, 1), (match.Capacity, match.Version));
    }

    private WebApplicationFactory<Program> HostWithLogs(CapturingLoggerProvider logs) =>
        factory.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(logs)));

    private HttpClient ClientFor(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Issue($"guard-{Guid.NewGuid():N}", now: factory.Clock.GetUtcNow()));
        return client;
    }

    private async Task<Guid> CreateMatchAsync(HttpClient client, int capacity)
    {
        var startsAt = factory.Clock.GetUtcNow().AddDays(2).ToString("O", CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, MatchesPath)
        {
            Content = new StringContent(
                $$"""{"title":"Pelada","startsAt":"{{startsAt}}","timeZone":"America/Sao_Paulo","durationMinutes":60,"capacity":{{capacity}},"visibility":"public"}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, string ifMatch, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{MatchesPath}/{id}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(request, Ct);
    }

    private static async Task GetUserIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/me", Ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task SeedRegistrationsAsync(WebApplicationFactory<Program> host, Guid matchId, IReadOnlyList<Guid> userIds)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IMatchesUnitOfWork>();
        var result = await unitOfWork.ExecuteAsync<bool>(
            async (session, ct) =>
            {
                var match = await session.GetMatchForUpdateAsync(matchId, ct);
                Assert.NotNull(match);
                foreach (var userId in userIds)
                {
                    session.AddRegistration(match.Register(userId, null, factory.Clock.GetUtcNow()).Value.Registration);
                }

                match.ClearDomainEvents();
                return true;
            },
            Ct);
        Assert.True(result.IsSuccess);
    }
}
