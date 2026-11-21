using System.Net;
using System.Text;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using static Applada.Api.IntegrationTests.Infrastructure.MatchesApi;
using static Applada.Api.IntegrationTests.Infrastructure.ProblemAssertions;

namespace Applada.Api.IntegrationTests;

/// <summary>
/// <c>GET /matches/{id}/registrations</c>: visível só ao organizador e a quem tem inscrição ativa (IDOR), paginada
/// por cursor e sem inscrições <c>withdrawn</c>.
/// </summary>
[Collection("api")]
public class ParticipantsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Organizer_sees_participants()
    {
        var (organizer, players, matchId) = await MatchWithThreePlayersAsync();
        using (organizer)
        {
            var page = await GetPageAsync(organizer.Client, matchId);

            AssertListsThreePlayers(page, players);
            Assert.Null(page.NextCursor);
        }

        DisposeAll(players);
    }

    [Fact]
    public async Task Confirmed_participant_sees_participants()
    {
        var (organizer, players, matchId) = await MatchWithThreePlayersAsync();
        using (organizer)
        {
            AssertListsThreePlayers(await GetPageAsync(players[0].Client, matchId), players);
        }

        DisposeAll(players);
    }

    [Fact]
    public async Task Active_participant_authorization_and_page_use_one_matches_statement()
    {
        var (organizer, players, matchId) = await MatchWithThreePlayersAsync();
        try
        {
            using var logs = new CapturingLoggerProvider();
            await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
            using var client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = players[0].Client.DefaultRequestHeaders.Authorization;
            logs.Clear();

            AssertListsThreePlayers(await GetPageAsync(client, matchId), players);

            var read = Assert.Single(logs.Entries, entry =>
                entry.Category == "Microsoft.EntityFrameworkCore.Database.Command"
                && (entry.Message.Contains("FROM matches.matches", StringComparison.Ordinal)
                    || entry.Message.Contains("FROM matches.registrations", StringComparison.Ordinal)));
            Assert.Contains("FROM matches.matches", read.Message, StringComparison.Ordinal);
            Assert.Contains("FROM matches.registrations", read.Message, StringComparison.Ordinal);
        }
        finally
        {
            organizer.Dispose();
            DisposeAll(players);
        }
    }

    [Fact]
    public async Task Organizer_with_no_registrations_gets_an_empty_page()
    {
        using var organizer = await NewUserAsync(factory, "part");
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 10);

        var page = await GetPageAsync(organizer.Client, matchId);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Waitlisted_participant_sees_participants()
    {
        var (organizer, players, matchId) = await MatchWithThreePlayersAsync();
        using (organizer)
        {
            AssertListsThreePlayers(await GetPageAsync(players[2].Client, matchId), players);
        }

        DisposeAll(players);
    }

    [Fact]
    public async Task Stranger_gets_403()
    {
        var (organizer, players, matchId) = await MatchWithThreePlayersAsync();
        using var stranger = await NewUserAsync(factory, "part");
        using (organizer)
        {
            using var response = await GetAsync(stranger.Client, matchId);

            using var problem = await AssertProblemAsync(response, HttpStatusCode.Forbidden, "participants-forbidden");
            var raw = problem.RootElement.GetRawText();
            Assert.All(players, p => Assert.DoesNotContain(p.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase));
        }

        DisposeAll(players);
    }

    [Fact]
    public async Task Withdrawn_user_gets_403_and_is_not_listed()
    {
        var (organizer, players, matchId) = await MatchWithThreePlayersAsync();
        using (organizer)
        {
            using (var withdrawn = await WithdrawAsync(players[0].Client, matchId))
            {
                Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);
            }

            using var response = await GetAsync(players[0].Client, matchId);
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "participants-forbidden");

            var page = await GetPageAsync(organizer.Client, matchId);
            Assert.Equal(
                [(players[1].Id, "confirmed", (int?)null), (players[2].Id, "confirmed", (int?)null)],
                page.Items);
        }

        DisposeAll(players);
    }

    [Fact]
    public async Task Pagination_walks_all_pages_without_duplicates()
    {
        using var organizer = await NewUserAsync(factory, "part");
        var players = new List<TestUser>();
        try
        {
            var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 20);
            foreach (var player in await NewUsersAsync(factory, 25, "part"))
            {
                players.Add(player);
                await RegisterExpectingAsync(player.Client, matchId, HttpStatusCode.Created);
            }

            var pages = new List<Page>();
            string? cursor = null;
            do
            {
                var page = await GetPageAsync(organizer.Client, matchId, $"limit=10{(cursor is null ? string.Empty : $"&cursor={cursor}")}");
                pages.Add(page);
                cursor = page.NextCursor;
            }
            while (cursor is not null && pages.Count < 10);

            Assert.Equal([10, 10, 5], pages.Select(p => p.Items.Count));
            var all = pages.SelectMany(p => p.Items).ToArray();
            Assert.Equal(25, all.Select(i => i.UserId).Distinct().Count());
            // Confirmados primeiro, depois a espera, cada grupo na ordem de chegada.
            var expected = players.Take(20).Select(p => (p.Id, "confirmed", (int?)null))
                .Concat(players.Skip(20).Select((p, i) => (p.Id, "waitlisted", (int?)(i + 1))));
            Assert.Equal(expected, all);
        }
        finally
        {
            DisposeAll(players);
        }
    }

    /// <summary>
    /// Capacidade 5 e 25 inscritos, limit 10: a primeira página termina dentro da espera, então a segunda e a terceira
    /// partem de um cursor de espera (rank 1). Sem buraco, sem repetição, na ordem e com posições contínuas.
    /// </summary>
    [Fact]
    public async Task Pagination_with_page_ending_inside_waitlist_walks_all_pages_in_order()
    {
        using var organizer = await NewUserAsync(factory, "part");
        var players = new List<TestUser>();
        try
        {
            var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 5);
            foreach (var player in await NewUsersAsync(factory, 25, "part"))
            {
                players.Add(player);
                await RegisterExpectingAsync(player.Client, matchId, HttpStatusCode.Created);
            }

            var pages = new List<Page>();
            string? cursor = null;
            do
            {
                var page = await GetPageAsync(organizer.Client, matchId, $"limit=10{(cursor is null ? string.Empty : $"&cursor={cursor}")}");
                pages.Add(page);
                cursor = page.NextCursor;
            }
            while (cursor is not null && pages.Count < 10);

            Assert.Equal([10, 10, 5], pages.Select(p => p.Items.Count));
            Assert.Equal(["confirmed", "waitlisted"], pages[0].Items.Select(i => i.Status).Distinct());
            Assert.All(pages.Skip(1).SelectMany(p => p.Items), i => Assert.Equal("waitlisted", i.Status));
            var expected = players.Take(5).Select(p => (p.Id, "confirmed", (int?)null))
                .Concat(players.Skip(5).Select((p, i) => (p.Id, "waitlisted", (int?)(i + 1))));
            Assert.Equal(expected, pages.SelectMany(p => p.Items));
        }
        finally
        {
            DisposeAll(players);
        }
    }

    [Fact]
    public async Task Limit_above_50_returns_400()
    {
        var (organizer, players, matchId) = await MatchWithThreePlayersAsync();
        using (organizer)
        {
            foreach (var limit in new[] { "51", "0", "-1", "abc", "1.5", "99999999999" })
            {
                using var response = await GetAsync(organizer.Client, matchId, $"limit={limit}");
                using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
                Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("limit", out _), $"limit={limit}");
            }

            Assert.Single((await GetPageAsync(organizer.Client, matchId, "limit=1")).Items);
            Assert.Equal(3, (await GetPageAsync(organizer.Client, matchId, "limit=50")).Items.Count);
        }

        DisposeAll(players);
    }

    /// <summary>Foco de revisão 1: cursor adulterado ou de outra listagem é 400 <c>invalid-cursor</c>, nunca 500.</summary>
    [Fact]
    public async Task Invalid_cursor_returns_400()
    {
        var (organizer, players, matchId) = await MatchWithThreePlayersAsync();
        using (organizer)
        {
            // Cursor legítimo de /me/matches (outra listagem).
            using var mine = await organizer.Client.GetAsync(new Uri($"{MyMatchesPath}?role=participant&limit=1", UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
            using var mineBody = await ReadJsonAsync(mine);
            var myMatchesCursor = mineBody.RootElement.GetProperty("nextCursor").GetString();
            Assert.NotNull(myMatchesCursor);

            string[] cursors =
            [
                "%%%",
                Base64Url("{}"),
                Base64Url("""{"k":"x","v":[]}"""),
                myMatchesCursor,
                CursorSamples.WithInvalidUtf8Key("participants"),
                Base64Url("""{"k":"\ud800","v":[0,1]}"""),
                // Escape mais longo que "participants": ValueEquals precisa desfazê-lo em vez de recusar pelo tamanho.
                Base64Url("""{"k":"\ud800\ud800\ud800","v":[0,1]}"""),
                Base64Url("""{"k":"participants","v":[]}"""),
                Base64Url("""{"k":"participants","v":[0]}"""),
                Base64Url("""{"k":"participants","v":[0,1,2]}"""),
                Base64Url("""{"k":"participants","v":["0","1"]}"""),
                Base64Url("""{"k":"participants","v":[2,1]}"""),
                Base64Url("""{"k":"participants","v":[0,0]}"""),
                Base64Url("""{"k":"participants","v":[0,1.5]}"""),
                Base64Url("""{"k":"participants","v":[0,99999999999999999999]}"""),
                Base64Url("""{"k":"participants","v":{"a":1}}"""),
                Base64Url("""{"k":"participants","v":[0,1],"x":1}"""),
                Base64Url("""{"k":1,"v":[0,1]}"""),
                Base64Url("""[["participants"]]"""),
                Base64Url("null"),
                Base64Url("não é json"),
                System.Buffers.Text.Base64Url.EncodeToString([0xFF, 0xFE, 0x00, 0x7B]),
                Base64Url(new string('[', 500) + new string(']', 500)),
                new string('A', 20_000),
                "eyJr",
            ];

            foreach (var cursor in cursors)
            {
                using var response = await GetAsync(organizer.Client, matchId, $"cursor={Uri.EscapeDataString(cursor)}");
                using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid-cursor");
            }

            // O cursor válido desta listagem continua funcionando.
            var first = await GetPageAsync(organizer.Client, matchId, "limit=1");
            Assert.NotNull(first.NextCursor);
            var second = await GetPageAsync(organizer.Client, matchId, $"limit=1&cursor={first.NextCursor}");
            Assert.Equal(players[1].Id, Assert.Single(second.Items).UserId);
        }

        DisposeAll(players);
    }

    // ---------- Apoio ----------

    /// <summary>Capacidade 2: os dois primeiros confirmam, o terceiro espera. O organizador também está inscrito em outra partida.</summary>
    private async Task<(TestUser Organizer, TestUser[] Players, Guid MatchId)> MatchWithThreePlayersAsync()
    {
        var organizer = await NewUserAsync(factory, "part");
        var players = new[] { await NewUserAsync(factory, "part"), await NewUserAsync(factory, "part"), await NewUserAsync(factory, "part") };
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 2);
        foreach (var player in players)
        {
            await RegisterExpectingAsync(player.Client, matchId, HttpStatusCode.Created);
        }

        // Duas partidas em que o organizador joga: garante um nextCursor em /me/matches?limit=1.
        foreach (var days in new[] { 3, 4 })
        {
            var other = await CreateMatchAsync(players[0].Client, factory.Clock.GetUtcNow().AddDays(days), capacity: 5);
            await RegisterExpectingAsync(organizer.Client, other, HttpStatusCode.Created);
        }

        return (organizer, players, matchId);
    }

    private static void AssertListsThreePlayers(Page page, TestUser[] players) =>
        Assert.Equal(
            [(players[0].Id, "confirmed", (int?)null), (players[1].Id, "confirmed", (int?)null), (players[2].Id, "waitlisted", (int?)1)],
            page.Items);

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid matchId, string query = "") =>
        client.GetAsync(new Uri($"{MatchesPath}/{matchId}/registrations{(query.Length > 0 ? "?" + query : string.Empty)}", UriKind.Relative), Ct);

    private static async Task<Page> GetPageAsync(HttpClient client, Guid matchId, string query = "")
    {
        using var response = await GetAsync(client, matchId, query);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {raw}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(raw);
        var items = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(i =>
            {
                Assert.Equal(4, i.EnumerateObject().Count()); // userId, status, position e displayName
                Assert.True(i.GetProperty("displayName").ValueKind is JsonValueKind.Null or JsonValueKind.String);
                var position = i.GetProperty("position");
                return (i.GetProperty("userId").GetGuid(), i.GetProperty("status").GetString()!, position.ValueKind == JsonValueKind.Null ? (int?)null : position.GetInt32());
            })
            .ToArray();
        var next = body.RootElement.GetProperty("nextCursor");
        return new Page(items, next.ValueKind == JsonValueKind.Null ? null : next.GetString());
    }

    private static string Base64Url(string json) => System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

    private static void DisposeAll(IEnumerable<TestUser> users)
    {
        foreach (var user in users)
        {
            user.Dispose();
        }
    }

    private sealed record Page(IReadOnlyList<(Guid UserId, string Status, int? Position)> Items, string? NextCursor);
}
