using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Api.IntegrationTests.Infrastructure;

/// <summary>Usuário de teste: cliente HTTP autenticado e o id interno já provisionado.</summary>
public sealed class TestUser(HttpClient client, Guid id) : IDisposable
{
    public HttpClient Client { get; } = client;

    public Guid Id { get; } = id;

    public void Dispose() => Client.Dispose();
}

/// <summary>Inscrição como está no banco.</summary>
public sealed record RegistrationRow(Guid UserId, RegistrationStatus Status, long QueueSeq, DateTimeOffset UpdatedAt);

/// <summary>Partida (contadores) e todas as suas inscrições, lidas do banco sem lock.</summary>
public sealed record MatchState(int Capacity, int ConfirmedCount, int WaitlistCount, IReadOnlyList<RegistrationRow> Rows)
{
    public IReadOnlyList<RegistrationRow> Confirmed => [.. Rows.Where(r => r.Status == RegistrationStatus.Confirmed).OrderBy(r => r.QueueSeq)];

    public IReadOnlyList<RegistrationRow> Waitlisted => [.. Rows.Where(r => r.Status == RegistrationStatus.Waitlisted).OrderBy(r => r.QueueSeq)];
}

/// <summary>Atalhos HTTP e de banco dos testes de inscrição, participantes, minhas partidas e concorrência.</summary>
public static class MatchesApi
{
    public const string MatchesPath = "/api/v1/matches";
    public const string MyMatchesPath = "/api/v1/me/matches";
    public const string SpotConfirmed = "matches.spot-confirmed.v1";
    public const string SpotReleased = "matches.spot-released.v1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string NewSubject(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    public static async Task<TestUser> NewUserAsync(ApiFactory factory, string prefix = "reg")
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = factory.CreateClientFor(NewSubject(prefix));
        return new TestUser(client, await GetUserIdAsync(client));
    }

    /// <summary>Provisiona <paramref name="count"/> usuários em paralelo.</summary>
    public static async Task<TestUser[]> NewUsersAsync(ApiFactory factory, int count, string prefix = "reg") =>
        await Task.WhenAll(Enumerable.Range(0, count).Select(_ => NewUserAsync(factory, prefix)));

    public static async Task<Guid> GetUserIdAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var response = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct);
        response.EnsureSuccessStatusCode();
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("userId").GetGuid();
    }

    public static async Task<Guid> CreateMatchAsync(HttpClient organizer, DateTimeOffset startsAt, int capacity)
    {
        ArgumentNullException.ThrowIfNull(organizer);
        var start = startsAt.ToString("O", CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, MatchesPath)
        {
            Content = new StringContent(
                $$"""{"title":"Pelada","startsAt":"{{start}}","timeZone":"America/Sao_Paulo","durationMinutes":60,"capacity":{{capacity}},"visibility":"public"}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        using var response = await organizer.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, Guid matchId)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.PostAsync(new Uri($"{MatchesPath}/{matchId}/registrations", UriKind.Relative), content: null, Ct);
    }

    public static Task<HttpResponseMessage> WithdrawAsync(HttpClient client, Guid matchId)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.DeleteAsync(new Uri($"{MatchesPath}/{matchId}/registrations/me", UriKind.Relative), Ct);
    }

    public static Task<HttpResponseMessage> PatchCapacityAsync(HttpClient client, Guid matchId, int version, int capacity)
    {
        ArgumentNullException.ThrowIfNull(client);
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{MatchesPath}/{matchId}")
        {
            Content = new StringContent($$"""{ "capacity": {{capacity}} }""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"W/\"{version}\"");
        return SendAndDisposeAsync(client, request);
    }

    /// <summary>Inscreve e confere o status HTTP esperado; devolve <c>status</c> e <c>position</c> do corpo.</summary>
    public static async Task<(string Status, int? Position)> RegisterExpectingAsync(HttpClient client, Guid matchId, HttpStatusCode expected)
    {
        using var response = await RegisterAsync(client, matchId);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(expected == response.StatusCode, $"Esperado {(int)expected}, veio {(int)response.StatusCode}: {raw}");
        using var body = JsonDocument.Parse(raw);
        return ReadRegistration(body.RootElement);
    }

    public static (string Status, int? Position) ReadRegistration(JsonElement root)
    {
        var position = root.GetProperty("position");
        return (root.GetProperty("status").GetString()!, position.ValueKind == JsonValueKind.Null ? null : position.GetInt32());
    }

    /// <summary><c>myRegistration</c> do <c>GET /matches/{id}</c>: nulo quando não há inscrição ativa.</summary>
    public static async Task<(string Status, int? Position)?> GetMyRegistrationAsync(HttpClient client, Guid matchId)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var response = await client.GetAsync(new Uri($"{MatchesPath}/{matchId}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var mine = body.RootElement.GetProperty("myRegistration");
        return mine.ValueKind == JsonValueKind.Null ? null : ReadRegistration(mine);
    }

    public static async Task<MatchState> ReadStateAsync(IServiceProvider services, Guid matchId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var match = await db.Matches.AsNoTracking().SingleAsync(m => m.Id == matchId, Ct);
        var rows = await db.Registrations.AsNoTracking()
            .Where(r => r.MatchId == matchId)
            .OrderBy(r => r.QueueSeq)
            .Select(r => new RegistrationRow(r.UserId, r.Status, r.QueueSeq, r.UpdatedAt))
            .ToListAsync(Ct);
        return new MatchState(match.Capacity, match.ConfirmedCount, match.WaitlistCount, rows);
    }

    /// <summary>Contadores iguais à contagem real das linhas, capacidade respeitada e <c>queue_seq</c> sem repetição.</summary>
    public static void AssertConsistent(MatchState state, string context = "")
    {
        ArgumentNullException.ThrowIfNull(state);
        Assert.True(state.ConfirmedCount == state.Confirmed.Count, $"{context} confirmed_count={state.ConfirmedCount}, linhas={state.Confirmed.Count}");
        Assert.True(state.WaitlistCount == state.Waitlisted.Count, $"{context} waitlist_count={state.WaitlistCount}, linhas={state.Waitlisted.Count}");
        Assert.True(state.ConfirmedCount <= state.Capacity, $"{context} confirmed_count={state.ConfirmedCount} > capacity={state.Capacity}");
        Assert.True(state.Rows.Select(r => r.QueueSeq).Distinct().Count() == state.Rows.Count, $"{context} queue_seq repetido");
    }

    /// <summary>Payloads (JSON) dos eventos do tipo dado gravados no outbox para a partida.</summary>
    public static async Task<IReadOnlyList<JsonElement>> OutboxPayloadsAsync(IServiceProvider services, Guid matchId, string eventType)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var payloads = await db.OutboxMessages.AsNoTracking()
            .Where(o => o.AggregateId == matchId && o.EventType == eventType)
            .Select(o => o.Payload)
            .ToListAsync(Ct);
        return [.. payloads.Select(p => JsonDocument.Parse(p).RootElement.Clone())];
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    public static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        ArgumentNullException.ThrowIfNull(response);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(status == response.StatusCode, $"Esperado {(int)status}, veio {(int)response.StatusCode}: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(raw);
        Assert.Equal($"urn:applada:problem:{code}", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal((int)status, problem.RootElement.GetProperty("status").GetInt32());
        return problem;
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request)
        {
            return await client.SendAsync(request, Ct);
        }
    }
}
