using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class MatchesLifecycleTests(ApiFactory factory)
{
    private const string MatchesPath = "/api/v1/matches";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Criação ----------

    [Fact]
    public async Task Create_returns_201_with_location_and_body()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var organizerId = await GetUserIdAsync(client);
        var startsAt = factory.Clock.GetUtcNow().AddDays(3);

        using var response = await PostMatchAsync(client, NewKey(), ValidBody(startsAt: startsAt, capacity: 12));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        var id = root.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/matches/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal(organizerId, root.GetProperty("organizerId").GetGuid());
        Assert.Equal("Pelada de quinta", root.GetProperty("title").GetString());
        Assert.Equal(startsAt, root.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal("America/Sao_Paulo", root.GetProperty("timeZone").GetString());
        Assert.Equal(90, root.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(12, root.GetProperty("capacity").GetInt32());
        Assert.Equal("public", root.GetProperty("visibility").GetString());
        Assert.Equal("scheduled", root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("confirmedCount").GetInt32());
        Assert.Equal(0, root.GetProperty("waitlistCount").GetInt32());
        Assert.Equal("Quadra 3", root.GetProperty("locationText").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("groupId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("venueId").ValueKind);
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        // O organizador não ocupa vaga automaticamente.
        Assert.Equal(JsonValueKind.Null, root.GetProperty("myRegistration").ValueKind);
    }

    [Fact]
    public async Task Create_without_idempotency_key_returns_400()
    {
        using var client = factory.CreateClientFor(NewSubject());

        using var response = await PostMatchAsync(client, idempotencyKey: null, ValidBody());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "idempotency-key-missing");
    }

    [Fact]
    public async Task Create_with_101_char_idempotency_key_returns_400()
    {
        using var client = factory.CreateClientFor(NewSubject());

        using var tooLong = await PostMatchAsync(client, new string('k', 101), ValidBody());
        await AssertProblemAsync(tooLong, HttpStatusCode.BadRequest, "idempotency-key-missing");

        using var atLimit = await PostMatchAsync(client, new string('k', 100), ValidBody());
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
    }

    [Fact]
    public async Task Create_replay_same_key_returns_same_body_and_single_match()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var organizerId = await GetUserIdAsync(client);
        var key = NewKey();
        var startsAt = factory.Clock.GetUtcNow().AddDays(3);
        var body = ValidBody(startsAt: startsAt);

        using var first = await PostMatchAsync(client, key, body);
        var firstBody = await first.Content.ReadAsStringAsync(Ct);
        // Mesmo conteúdo com outra ordem de campos e outro espaçamento: é a mesma requisição.
        var reordered = $$"""
            {
              "capacity": 10,   "visibility": "public",
              "durationMinutes": 90, "timeZone": "America/Sao_Paulo",
              "startsAt": "{{startsAt:O}}", "title": "Pelada de quinta", "locationText": "Quadra 3"
            }
            """;
        using var second = await PostMatchAsync(client, key, reordered);
        var secondBody = await second.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(1, await CountMatchesOfAsync(organizerId));
    }

    [Fact]
    public async Task Create_same_key_different_payload_returns_422()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var organizerId = await GetUserIdAsync(client);
        var key = NewKey();

        using var first = await PostMatchAsync(client, key, ValidBody(capacity: 10));
        using var second = await PostMatchAsync(client, key, ValidBody(capacity: 11));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await AssertProblemAsync(second, HttpStatusCode.UnprocessableEntity, "idempotency-key-reused");
        Assert.Equal(1, await CountMatchesOfAsync(organizerId));
    }

    [Fact]
    public async Task Same_key_from_different_users_creates_distinct_matches()
    {
        // A chave é por usuário: outro usuário nunca recebe a resposta guardada de alguém.
        using var alice = factory.CreateClientFor(NewSubject());
        using var bob = factory.CreateClientFor(NewSubject());
        var key = NewKey();

        using var fromAlice = await PostMatchAsync(alice, key, ValidBody());
        using var fromBob = await PostMatchAsync(bob, key, ValidBody());

        Assert.Equal(HttpStatusCode.Created, fromAlice.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fromBob.StatusCode);
        Assert.NotEqual(fromAlice.Headers.Location, fromBob.Headers.Location);
        using var bobBody = await ReadJsonAsync(fromBob);
        Assert.Equal(await GetUserIdAsync(bob), bobBody.RootElement.GetProperty("organizerId").GetGuid());
    }

    [Fact]
    public async Task Create_invalid_payload_returns_400_with_errors()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var organizerId = await GetUserIdAsync(client);

        using var response = await PostMatchAsync(client, NewKey(), ValidBody(capacity: 0, timeZone: "Brasil/Inexistente"));

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("capacity", out _));
        Assert.True(errors.TryGetProperty("timeZone", out _));
        Assert.Equal(0, await CountMatchesOfAsync(organizerId));
    }

    [Fact]
    public async Task Create_starts_at_boundaries()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var now = factory.Clock.GetUtcNow();

        using var past = await PostMatchAsync(client, NewKey(), ValidBody(startsAt: now.AddSeconds(-1)));
        using var exactlyMax = await PostMatchAsync(client, NewKey(), ValidBody(startsAt: now.AddDays(365)));
        using var beyondMax = await PostMatchAsync(client, NewKey(), ValidBody(startsAt: now.AddDays(365).AddSeconds(1)));

        using var pastProblem = await AssertProblemAsync(past, HttpStatusCode.BadRequest, "validation");
        Assert.True(pastProblem.RootElement.GetProperty("errors").TryGetProperty("startsAt", out _));
        Assert.Equal(HttpStatusCode.Created, exactlyMax.StatusCode);
        using var beyondProblem = await AssertProblemAsync(beyondMax, HttpStatusCode.BadRequest, "validation");
        Assert.True(beyondProblem.RootElement.GetProperty("errors").TryGetProperty("startsAt", out _));
    }

    [Fact]
    public async Task Create_concurrent_same_key_creates_one_match()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var organizerId = await GetUserIdAsync(client);
        var key = NewKey();
        var body = ValidBody();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var response = await PostMatchAsync(client, key, body);
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync(Ct));
        }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var ids = responses.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("id").GetGuid()).Distinct().ToArray();
        Assert.Single(ids);
        Assert.Single(responses.Select(r => r.Body).Distinct());
        Assert.Equal(1, await CountMatchesOfAsync(organizerId));
        Assert.Equal(1, await CountOutboxAsync(ids[0], "matches.match-created.v1"));
    }

    [Fact]
    public async Task Create_with_malformed_json_returns_400_problem()
    {
        using var client = factory.CreateClientFor(NewSubject());

        using var response = await PostMatchAsync(client, NewKey(), """{ "title": "Pelada", "capacity": """);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Create_with_wrong_type_returns_400_with_field_error()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var body = ValidBody().Replace("\"capacity\":10", "\"capacity\":\"dez\"", StringComparison.Ordinal);

        using var response = await PostMatchAsync(client, NewKey(), body);

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("capacity", out _));
    }

    [Fact]
    public async Task Create_with_lone_surrogate_in_title_returns_400_problem()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var body = ValidBody().Replace("\"title\":\"Pelada de quinta\"", "\"title\":\"Pelada \\ud800\"", StringComparison.Ordinal);
        Assert.Contains("\\ud800", body, StringComparison.Ordinal);

        using var response = await PostMatchAsync(client, NewKey(), body);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Create_with_null_body_returns_400_problem()
    {
        using var client = factory.CreateClientFor(NewSubject());

        using var response = await PostMatchAsync(client, NewKey(), "null");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Create_replay_within_24h_returns_stored_response()
    {
        var subject = NewSubject();
        using var client = factory.CreateClientFor(subject);
        var organizerId = await GetUserIdAsync(client);
        var key = NewKey();
        var body = ValidBody();
        using var first = await PostMatchAsync(client, key, body);
        var firstBody = await first.Content.ReadAsStringAsync(Ct);

        await WithClockAdvancedAsync(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1), async () =>
        {
            using var later = factory.CreateClientFor(subject);
            using var replay = await PostMatchAsync(later, key, body);

            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(firstBody, await replay.Content.ReadAsStringAsync(Ct));
            Assert.Equal(1, await CountMatchesOfAsync(organizerId));
        });
    }

    [Fact]
    public async Task Create_with_key_older_than_24h_creates_new_match()
    {
        var subject = NewSubject();
        using var client = factory.CreateClientFor(subject);
        var organizerId = await GetUserIdAsync(client);
        var key = NewKey();
        var body = ValidBody();
        using var first = await PostMatchAsync(client, key, body);
        using var firstJson = await ReadJsonAsync(first);
        var firstId = firstJson.RootElement.GetProperty("id").GetGuid();

        await WithClockAdvancedAsync(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1), async () =>
        {
            using var later = factory.CreateClientFor(subject);

            // Várias ao mesmo tempo com a chave vencida: uma só partida nova, todas com a mesma resposta.
            var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
            {
                using var response = await PostMatchAsync(later, key, body);
                return (response.StatusCode, Body: await response.Content.ReadAsStringAsync(Ct));
            }));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            Assert.Single(responses.Select(r => r.Body).Distinct());
            var newId = JsonDocument.Parse(responses[0].Body).RootElement.GetProperty("id").GetGuid();
            Assert.NotEqual(firstId, newId);
            Assert.Equal(2, await CountMatchesOfAsync(organizerId));

            // A chave agora guarda a resposta nova.
            using var replay = await PostMatchAsync(later, key, body);
            Assert.Equal(responses[0].Body, await replay.Content.ReadAsStringAsync(Ct));
        });
    }

    [Fact]
    public async Task Create_with_key_older_than_24h_and_different_payload_returns_201()
    {
        var subject = NewSubject();
        using var client = factory.CreateClientFor(subject);
        var organizerId = await GetUserIdAsync(client);
        var key = NewKey();
        var startsAt = factory.Clock.GetUtcNow().AddDays(3);
        using var first = await PostMatchAsync(client, key, ValidBody(startsAt: startsAt, capacity: 10));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        await WithClockAdvancedAsync(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1), async () =>
        {
            using var later = factory.CreateClientFor(subject);
            using var response = await PostMatchAsync(later, key, ValidBody(startsAt: startsAt, capacity: 11));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var json = await ReadJsonAsync(response);
            Assert.Equal(11, json.RootElement.GetProperty("capacity").GetInt32());
            Assert.Equal(2, await CountMatchesOfAsync(organizerId));
        });
    }

    [Fact]
    public async Task Create_with_body_over_16kb_returns_413_problem()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var organizerId = await GetUserIdAsync(client);
        var huge = ValidBody().Replace("\"locationText\":\"Quadra 3\"", $"\"locationText\":\"{new string('x', 17 * 1024)}\"", StringComparison.Ordinal);

        using var response = await PostMatchAsync(client, NewKey(), huge);

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "payload-too-large");
        Assert.Equal(0, await CountMatchesOfAsync(organizerId));
    }

    [Fact]
    public async Task Create_with_chunked_body_over_16kb_returns_413_problem()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var huge = ValidBody().Replace("\"locationText\":\"Quadra 3\"", $"\"locationText\":\"{new string('x', 17 * 1024)}\"", StringComparison.Ordinal);
        using var request = new HttpRequestMessage(HttpMethod.Post, MatchesPath)
        {
            // Sem Content-Length: o limite vale também para corpo em streaming.
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(huge))),
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.TransferEncodingChunked = true;
        request.Headers.Add("Idempotency-Key", NewKey());

        using var response = await client.SendAsync(request, Ct);

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "payload-too-large");
    }

    [Fact]
    public async Task Patch_with_body_over_16kb_returns_413_problem()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer);

        using var response = await PatchAsync(organizer, id, "W/\"1\"", $$"""{ "title": "{{new string('t', 17 * 1024)}}" }""");

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "payload-too-large");
    }

    // ---------- Consulta ----------

    [Fact]
    public async Task Get_returns_etag_and_no_store()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer);
        using var other = factory.CreateClientFor(NewSubject());

        foreach (var client in new[] { organizer, other })
        {
            using var response = await client.GetAsync($"{MatchesPath}/{id}", Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("W/\"1\"", response.Headers.ETag?.ToString());
            Assert.True(response.Headers.CacheControl?.Private);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var body = await ReadJsonAsync(response);
            Assert.Equal(id, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(1, body.RootElement.GetProperty("version").GetInt32());
        }
    }

    [Fact]
    public async Task Get_unknown_returns_404_problem()
    {
        using var client = factory.CreateClientFor(NewSubject());

        using var response = await client.GetAsync($"{MatchesPath}/{Guid.CreateVersion7()}", Ct);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "match-not-found");
    }

    [Fact]
    public async Task Get_shows_my_registration_with_waitlist_position()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer, capacity: 1);
        using var first = factory.CreateClientFor(NewSubject());
        using var second = factory.CreateClientFor(NewSubject());
        using var third = factory.CreateClientFor(NewSubject());
        await SeedRegistrationsAsync(id, [await GetUserIdAsync(first), await GetUserIdAsync(second), await GetUserIdAsync(third)]);

        using var confirmed = await ReadJsonAsync(await first.GetAsync($"{MatchesPath}/{id}", Ct));
        using var waitlisted = await ReadJsonAsync(await third.GetAsync($"{MatchesPath}/{id}", Ct));
        using var outsider = await ReadJsonAsync(await organizer.GetAsync($"{MatchesPath}/{id}", Ct));

        var mine = confirmed.RootElement.GetProperty("myRegistration");
        Assert.Equal("confirmed", mine.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("position").ValueKind);
        var queued = waitlisted.RootElement.GetProperty("myRegistration");
        Assert.Equal("waitlisted", queued.GetProperty("status").GetString());
        Assert.Equal(2, queued.GetProperty("position").GetInt32());
        Assert.Equal(JsonValueKind.Null, outsider.RootElement.GetProperty("myRegistration").ValueKind);
        Assert.Equal(1, outsider.RootElement.GetProperty("confirmedCount").GetInt32());
        Assert.Equal(2, outsider.RootElement.GetProperty("waitlistCount").GetInt32());
    }

    // ---------- Edição e cancelamento ----------

    [Fact]
    public async Task Patch_by_organizer_returns_200_with_new_version()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer);

        using var response = await PatchAsync(organizer, id, "W/\"1\"", """{ "title": "Pelada nova", "locationText": "" }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("W/\"2\"", response.Headers.ETag?.ToString());
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("Pelada nova", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("locationText").ValueKind);
        Assert.Equal(10, body.RootElement.GetProperty("capacity").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Patch_by_non_organizer_returns_403()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer);
        using var intruder = factory.CreateClientFor(NewSubject());

        using var response = await PatchAsync(intruder, id, "W/\"1\"", """{ "title": "Invadida" }""");

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "not-organizer");
        using var current = await ReadJsonAsync(await organizer.GetAsync($"{MatchesPath}/{id}", Ct));
        Assert.Equal("Pelada de quinta", current.RootElement.GetProperty("title").GetString());
        Assert.Equal(1, current.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Patch_unknown_match_returns_404()
    {
        using var client = factory.CreateClientFor(NewSubject());

        using var response = await PatchAsync(client, Guid.CreateVersion7(), "W/\"1\"", """{ "title": "X" }""");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "match-not-found");
    }

    [Fact]
    public async Task Patch_without_if_match_returns_428()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer);

        using var response = await PatchAsync(organizer, id, ifMatch: null, """{ "title": "Sem If-Match" }""");

        await AssertProblemAsync(response, HttpStatusCode.PreconditionRequired, "if-match-required");
    }

    [Fact]
    public async Task Patch_with_stale_etag_returns_412()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer);

        using var first = await PatchAsync(organizer, id, "\"1\"", """{ "title": "Primeira edição" }""");
        using var stale = await PatchAsync(organizer, id, "W/\"1\"", """{ "title": "Edição atrasada" }""");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, "version-mismatch");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("*")]
    [InlineData("W/1")]
    [InlineData("\"1\", \"2\"")]
    [InlineData("\"99999999999\"")]
    public async Task Patch_with_unparsable_if_match_returns_412(string ifMatch)
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer);

        using var response = await PatchAsync(organizer, id, ifMatch, """{ "title": "X" }""");

        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed, "version-mismatch");
    }

    [Fact]
    public async Task Patch_with_invalid_values_returns_400_with_errors()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer);

        using var invalid = await PatchAsync(organizer, id, "W/\"1\"", """{ "title": "", "capacity": 201 }""");
        using var malformed = await PatchAsync(organizer, id, "W/\"1\"", """{ "capacity": """);

        using var problem = await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "validation");
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("title", out _));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("capacity", out _));
        await AssertProblemAsync(malformed, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Patch_capacity_below_confirmed_returns_409()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer, capacity: 3);
        await SeedRegistrationsAsync(id, [Guid.CreateVersion7(), Guid.CreateVersion7()]);

        using var response = await PatchAsync(organizer, id, "W/\"1\"", """{ "capacity": 1 }""");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "capacity-below-confirmed");
    }

    [Fact]
    public async Task Patch_capacity_increase_promotes_waitlist()
    {
        // As rotas de inscrição chegam na Tarefa 11: as inscrições entram pelo domínio e pela unidade de trabalho.
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer, capacity: 1);
        var users = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        await SeedRegistrationsAsync(id, users);

        using var response = await PatchAsync(organizer, id, "W/\"1\"", """{ "capacity": 3 }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(3, body.RootElement.GetProperty("capacity").GetInt32());
        Assert.Equal(3, body.RootElement.GetProperty("confirmedCount").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("waitlistCount").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("version").GetInt32());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var statuses = await db.Registrations.AsNoTracking()
            .Where(r => r.MatchId == id)
            .OrderBy(r => r.QueueSeq)
            .Select(r => r.Status)
            .ToListAsync(Ct);
        Assert.Equal(
            [RegistrationStatus.Confirmed, RegistrationStatus.Confirmed, RegistrationStatus.Confirmed, RegistrationStatus.Waitlisted],
            statuses);

        // Promoções viram spot-confirmed com source waitlist_promotion. A ordem FIFO está nos status acima; entre
        // eventos o outbox não garante ordem (mesmo occurred_at), então os promovidos são comparados como conjunto.
        var promoted = await db.OutboxMessages.AsNoTracking()
            .Where(o => o.AggregateId == id && o.EventType == "matches.spot-confirmed.v1")
            .Select(o => o.Payload)
            .ToListAsync(Ct);
        Assert.Equal(2, promoted.Count);
        var payloads = promoted.Select(p => JsonDocument.Parse(p).RootElement).ToArray();
        Assert.All(payloads, p => Assert.Equal("waitlist_promotion", p.GetProperty("source").GetString()));
        Assert.Equal(users[1..3].Order(), payloads.Select(p => p.GetProperty("userId").GetGuid()).Order());
    }

    [Fact]
    public async Task Cancel_by_non_organizer_returns_403()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer);
        using var intruder = factory.CreateClientFor(NewSubject());

        using var response = await intruder.PostAsync($"{MatchesPath}/{id}/cancellation", content: null, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "not-organizer");
        using var current = await ReadJsonAsync(await organizer.GetAsync($"{MatchesPath}/{id}", Ct));
        Assert.Equal("scheduled", current.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancel_unknown_match_returns_404()
    {
        using var client = factory.CreateClientFor(NewSubject());

        using var response = await client.PostAsync($"{MatchesPath}/{Guid.CreateVersion7()}/cancellation", content: null, Ct);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "match-not-found");
    }

    [Fact]
    public async Task Cancel_then_get_shows_cancelled()
    {
        using var organizer = factory.CreateClientFor(NewSubject());
        var id = await CreateMatchAsync(organizer);

        using var cancelled = await organizer.PostAsync($"{MatchesPath}/{id}/cancellation", content: null, Ct);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("W/\"2\"", cancelled.Headers.ETag?.ToString());
        using (var body = await ReadJsonAsync(cancelled))
        {
            Assert.Equal("cancelled", body.RootElement.GetProperty("status").GetString());
            Assert.Equal(2, body.RootElement.GetProperty("version").GetInt32());
        }

        using var get = await organizer.GetAsync($"{MatchesPath}/{id}", Ct);
        using (var body = await ReadJsonAsync(get))
        {
            Assert.Equal("cancelled", body.RootElement.GetProperty("status").GetString());
        }

        Assert.Equal(1, await CountOutboxAsync(id, "matches.match-cancelled.v1"));

        // Partida cancelada não aceita mais alteração.
        using var again = await organizer.PostAsync($"{MatchesPath}/{id}/cancellation", content: null, Ct);
        await AssertProblemAsync(again, HttpStatusCode.Conflict, "match-cancelled");
        using var patch = await PatchAsync(organizer, id, "W/\"2\"", """{ "title": "Tarde demais" }""");
        await AssertProblemAsync(patch, HttpStatusCode.Conflict, "match-cancelled");
    }

    // ---------- Outbox e autenticação ----------

    [Fact]
    public async Task Create_writes_match_created_to_outbox()
    {
        using var client = factory.CreateClientFor(NewSubject());
        var organizerId = await GetUserIdAsync(client);
        var startsAt = factory.Clock.GetUtcNow().AddDays(5);
        var id = await CreateMatchAsync(client, capacity: 14, startsAt: startsAt);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(o => o.AggregateId == id, Ct);

        Assert.Equal("matches.match-created.v1", message.EventType);
        Assert.Equal(1, message.SchemaVersion);
        Assert.Null(message.PublishedAt);
        using var payload = JsonDocument.Parse(message.Payload);
        var root = payload.RootElement;
        Assert.Equal(id, root.GetProperty("matchId").GetGuid());
        Assert.Equal(organizerId, root.GetProperty("organizerId").GetGuid());
        Assert.Equal(startsAt.UtcTicks / 10, root.GetProperty("startsAt").GetDateTimeOffset().UtcTicks / 10);
        Assert.Equal(14, root.GetProperty("capacity").GetInt32());
        Assert.Equal("public", root.GetProperty("visibility").GetString());
    }

    [Fact]
    public async Task Matches_routes_require_authentication()
    {
        using var anonymous = factory.CreateClient();
        var id = Guid.CreateVersion7();

        using var get = await anonymous.GetAsync($"{MatchesPath}/{id}", Ct);
        using var post = await PostMatchAsync(anonymous, NewKey(), ValidBody());
        using var patch = await PatchAsync(anonymous, id, "W/\"1\"", "{}");
        using var cancel = await anonymous.PostAsync($"{MatchesPath}/{id}/cancellation", content: null, Ct);

        Assert.All([get, post, patch, cancel], r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    // ---------- Apoio ----------

    private static string NewSubject() => $"matches-{Guid.NewGuid():N}";

    private static string NewKey() => $"key-{Guid.NewGuid():N}";

    private string ValidBody(DateTimeOffset? startsAt = null, int capacity = 10, string timeZone = "America/Sao_Paulo")
    {
        var start = (startsAt ?? factory.Clock.GetUtcNow().AddDays(3)).ToString("O", CultureInfo.InvariantCulture);
        return $$"""{"title":"Pelada de quinta","startsAt":"{{start}}","timeZone":"{{timeZone}}","durationMinutes":90,"capacity":{{capacity}},"visibility":"public","locationText":"Quadra 3"}""";
    }

    private static async Task<HttpResponseMessage> PostMatchAsync(HttpClient client, string? idempotencyKey, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MatchesPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, string? ifMatch, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{MatchesPath}/{id}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<Guid> CreateMatchAsync(HttpClient client, int capacity = 10, DateTimeOffset? startsAt = null)
    {
        using var response = await PostMatchAsync(client, NewKey(), ValidBody(startsAt: startsAt, capacity: capacity));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Inscreve na ordem dada, pelo domínio e pela unidade de trabalho (sem rota de inscrição ainda).</summary>
    private async Task SeedRegistrationsAsync(Guid matchId, IReadOnlyList<Guid> userIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
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

    private static async Task<Guid> GetUserIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/me", Ct);
        response.EnsureSuccessStatusCode();
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("userId").GetGuid();
    }

    private async Task<int> CountMatchesOfAsync(Guid organizerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        return await db.Matches.CountAsync(m => m.OrganizerId == organizerId, Ct);
    }

    private async Task<int> CountOutboxAsync(Guid aggregateId, string eventType)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        return await db.OutboxMessages.CountAsync(o => o.AggregateId == aggregateId && o.EventType == eventType, Ct);
    }

    /// <summary>Avança o relógio da fábrica durante <paramref name="action"/> e sempre o devolve depois.</summary>
    private async Task WithClockAdvancedAsync(TimeSpan delta, Func<Task> action)
    {
        factory.Clock.Advance(delta);
        try
        {
            await action();
        }
        finally
        {
            factory.Clock.Advance(-delta);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

    private static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(status == response.StatusCode, $"Esperado {(int)status}, veio {(int)response.StatusCode}: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(raw);
        Assert.Equal($"urn:applada:problem:{code}", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal((int)status, problem.RootElement.GetProperty("status").GetInt32());
        return problem;
    }
}
