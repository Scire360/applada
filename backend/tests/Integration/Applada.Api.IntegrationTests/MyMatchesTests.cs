using System.Net;
using System.Text;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using static Applada.Api.IntegrationTests.Infrastructure.MatchesApi;

namespace Applada.Api.IntegrationTests;

/// <summary><c>GET /me/matches?role=organizer|participant</c>: partidas do próprio usuário por papel e cursor.</summary>
[Collection("api")]
public class MyMatchesTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Organizer_cursor_pages_from_started_match_to_ordered_future_matches()
    {
        var subject = NewSubject("mine");
        var now = factory.Clock.GetUtcNow();
        Guid started, day1, day2a, day2b, day3, cancelled;
        using (var organizer = factory.CreateClientFor(subject))
        using (var other = await NewUserAsync(factory, "mine"))
        {
            day3 = await CreateMatchAsync(organizer, now.AddDays(3), capacity: 4);
            day2a = await CreateMatchAsync(organizer, now.AddDays(2), capacity: 4);
            day1 = await CreateMatchAsync(organizer, now.AddDays(1), capacity: 4);
            day2b = await CreateMatchAsync(organizer, now.AddDays(2), capacity: 6);
            cancelled = await CreateMatchAsync(organizer, now.AddDays(4), capacity: 4);
            started = await CreateMatchAsync(organizer, now.AddMinutes(10), capacity: 4);
            await CreateMatchAsync(other.Client, now.AddDays(1), capacity: 4); // de outro organizador
            using (var cancel = await organizer.PostAsync(new Uri($"{MatchesPath}/{cancelled}/cancellation", UriKind.Relative), null, Ct))
            {
                Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            }

            // O organizador também joga numa delas; a partida cheia de outro jogador mostra os contadores.
            await RegisterExpectingAsync(organizer, day2b, HttpStatusCode.Created);
            await RegisterExpectingAsync(other.Client, day2b, HttpStatusCode.Created);
        }

        var delta = TimeSpan.FromMinutes(70);
        factory.Clock.Advance(delta);
        try
        {
            using var organizer = factory.CreateClientFor(subject);
            var pages = await WalkAsync(organizer, "role=organizer", limit: 1);

            // Mesmo startsAt: desempate pelo id, na ordem do PostgreSQL (bytes do uuid = texto hexadecimal).
            var sameDay = new[] { day2a, day2b }.OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
            Guid[] expected = [started, day1, sameDay[0], sameDay[1], day3, cancelled];
            var all = pages.Select(p => Assert.Single(p.Items)).ToArray();
            Assert.Equal(expected, all.Select(i => i.Id));

            var withPlayers = all.Single(i => i.Id == day2b);
            Assert.Equal(("scheduled", 2, 6, "confirmed"), (withPlayers.Status, withPlayers.ConfirmedCount, withPlayers.Capacity, withPlayers.MyStatus));
            var cancelledItem = all.Single(i => i.Id == cancelled);
            Assert.Equal(("cancelled", (string?)null), (cancelledItem.Status, cancelledItem.MyStatus));
            Assert.Equal("Pelada", all[0].Title);
            Assert.Equal(now.AddMinutes(10).UtcTicks / 10, all[0].StartsAt.UtcTicks / 10);
        }
        finally
        {
            factory.Clock.Advance(-delta);
        }
    }

    [Fact]
    public async Task Started_match_is_visible_only_to_its_organizer_until_attendance_closes()
    {
        var organizerSubject = NewSubject("mine");
        var playerSubject = NewSubject("mine");
        using var organizer = factory.CreateClientFor(organizerSubject);
        using var player = factory.CreateClientFor(playerSubject);
        var now = factory.Clock.GetUtcNow();
        var startsAt = DateTimeOffset.FromUnixTimeSeconds(now.AddHours(1).ToUnixTimeSeconds());
        const int durationMinutes = 600;
        var scheduled = await CreateMatchAsync(organizer, startsAt, capacity: 4, durationMinutes: durationMinutes);
        var cancelled = await CreateMatchAsync(organizer, startsAt, capacity: 4);
        await CreateMatchAsync(player, startsAt, capacity: 4);
        await RegisterExpectingAsync(player, scheduled, HttpStatusCode.Created);
        using var cancel = await organizer.PostAsync(new Uri($"{MatchesPath}/{cancelled}/cancellation", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        try
        {
            factory.Clock.SetUtcNow(startsAt);
            using var atStart = factory.CreateClientFor(organizerSubject);
            var upcoming = Assert.Single(await WalkAsync(atStart, "role=organizer", limit: 20));
            Assert.Contains(upcoming.Items, i => i.Id == cancelled);

            factory.Clock.SetUtcNow(startsAt.AddHours(1));
            using var participant = factory.CreateClientFor(playerSubject);
            var participating = Assert.Single(await WalkAsync(participant, "role=participant", limit: 20));
            Assert.Empty(participating.Items);
            using var duringMatch = factory.CreateClientFor(organizerSubject);
            var organized = Assert.Single(await WalkAsync(duringMatch, "role=organizer", limit: 20));
            Assert.Equal(scheduled, Assert.Single(organized.Items).Id);

            var closesAt = startsAt.AddMinutes(durationMinutes).AddHours(24);
            factory.Clock.SetUtcNow(closesAt.AddTicks(-10));
            using var nearClose = factory.CreateClientFor(organizerSubject);
            var lastOpen = Assert.Single(await WalkAsync(nearClose, "role=organizer", limit: 20));
            Assert.Equal(scheduled, Assert.Single(lastOpen.Items).Id);
            foreach (var instant in new[] { closesAt, closesAt.AddTicks(10) })
            {
                factory.Clock.SetUtcNow(instant);
                var closed = Assert.Single(await WalkAsync(nearClose, "role=organizer", limit: 20));
                Assert.Empty(closed.Items);
            }
        }
        finally
        {
            factory.Clock.SetUtcNow(now);
        }
    }

    [Fact]
    public async Task Participant_role_lists_active_registrations_only()
    {
        using var organizer = await NewUserAsync(factory, "mine");
        using var player = await NewUserAsync(factory, "mine");
        using var filler = await NewUserAsync(factory, "mine");
        var now = factory.Clock.GetUtcNow();
        var confirmedIn = await CreateMatchAsync(organizer.Client, now.AddDays(1), capacity: 5);
        var waitlistedIn = await CreateMatchAsync(organizer.Client, now.AddDays(2), capacity: 1);
        var withdrawnFrom = await CreateMatchAsync(organizer.Client, now.AddDays(3), capacity: 5);
        var ownMatch = await CreateMatchAsync(player.Client, now.AddDays(1).AddHours(1), capacity: 5);
        await RegisterExpectingAsync(player.Client, confirmedIn, HttpStatusCode.Created);
        await RegisterExpectingAsync(filler.Client, waitlistedIn, HttpStatusCode.Created);
        await RegisterExpectingAsync(player.Client, waitlistedIn, HttpStatusCode.Created);
        await RegisterExpectingAsync(player.Client, withdrawnFrom, HttpStatusCode.Created);
        using (var withdrawn = await WithdrawAsync(player.Client, withdrawnFrom))
        {
            Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);
        }

        var participating = (await WalkAsync(player.Client, "role=participant", limit: 20)).SelectMany(p => p.Items).ToArray();

        Assert.Equal(
            [(confirmedIn, "confirmed", 1, 5), (waitlistedIn, "waitlisted", 1, 1)],
            participating.Select(i => (i.Id, i.MyStatus, i.ConfirmedCount, i.Capacity)));

        var organized = (await WalkAsync(player.Client, "role=organizer", limit: 20)).SelectMany(p => p.Items).ToArray();
        Assert.Equal([(ownMatch, (string?)null)], organized.Select(i => (i.Id, i.MyStatus)));
    }

    [Fact]
    public async Task Never_returns_other_users_matches()
    {
        using var organizer = await NewUserAsync(factory, "mine");
        using var player = await NewUserAsync(factory, "mine");
        using var stranger = await NewUserAsync(factory, "mine");
        var now = factory.Clock.GetUtcNow();
        foreach (var days in new[] { 1, 2, 3 })
        {
            var matchId = await CreateMatchAsync(organizer.Client, now.AddDays(days), capacity: 5);
            await RegisterExpectingAsync(player.Client, matchId, HttpStatusCode.Created);
        }

        // Cursores de outro usuário só posicionam a leitura; o filtro continua sendo o usuário autenticado.
        var organizerCursor = (await WalkAsync(organizer.Client, "role=organizer", limit: 1))[0].NextCursor;
        var playerCursor = (await WalkAsync(player.Client, "role=participant", limit: 1))[0].NextCursor;
        Assert.NotNull(organizerCursor);
        Assert.NotNull(playerCursor);

        foreach (var query in new[] { "role=organizer", "role=participant", $"role=organizer&cursor={organizerCursor}", $"role=participant&cursor={playerCursor}" })
        {
            var page = Assert.Single(await WalkAsync(stranger.Client, query, limit: 50));
            Assert.Empty(page.Items);
            Assert.Null(page.NextCursor);
        }

        Assert.Equal(3, (await WalkAsync(organizer.Client, "role=organizer", limit: 50)).SelectMany(p => p.Items).Count());
        Assert.Empty((await WalkAsync(organizer.Client, "role=participant", limit: 50)).SelectMany(p => p.Items));
        Assert.Empty((await WalkAsync(player.Client, "role=organizer", limit: 50)).SelectMany(p => p.Items));
    }

    [Fact]
    public async Task Invalid_role_returns_400()
    {
        using var user = await NewUserAsync(factory, "mine");

        foreach (var query in new[] { string.Empty, "role=", "role=admin", "role=ORGANIZER", "role=organizer&role=participant" })
        {
            using var response = await user.Client.GetAsync(new Uri($"{MyMatchesPath}?{query}", UriKind.Relative), Ct);
            using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
            Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("role", out _), $"query '{query}'");
        }

        using var tooMany = await user.Client.GetAsync(new Uri($"{MyMatchesPath}?role=organizer&limit=51", UriKind.Relative), Ct);
        using var limitProblem = await AssertProblemAsync(tooMany, HttpStatusCode.BadRequest, "validation");
        Assert.True(limitProblem.RootElement.GetProperty("errors").TryGetProperty("limit", out _));
    }

    [Fact]
    public async Task Invalid_cursor_returns_400()
    {
        using var organizer = await NewUserAsync(factory, "mine");
        using var first = await NewUserAsync(factory, "mine");
        using var second = await NewUserAsync(factory, "mine");
        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(1), capacity: 5);
        await RegisterExpectingAsync(first.Client, matchId, HttpStatusCode.Created);
        await RegisterExpectingAsync(second.Client, matchId, HttpStatusCode.Created);

        // Cursor legítimo da listagem de participantes.
        using var participants = await organizer.Client.GetAsync(new Uri($"{MatchesPath}/{matchId}/registrations?limit=1", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, participants.StatusCode);
        using var participantsBody = await ReadJsonAsync(participants);
        var participantsCursor = participantsBody.RootElement.GetProperty("nextCursor").GetString();
        Assert.NotNull(participantsCursor);

        string[] cursors =
        [
            "%%%",
            participantsCursor,
            Base64Url("{}"),
            Base64Url("""{"k":"my-matches","v":[]}"""),
            Base64Url("""{"k":"my-matches","v":["2026-01-01T00:00:00+00:00"]}"""),
            Base64Url("""{"k":"my-matches","v":["ontem","00000000-0000-0000-0000-000000000000"]}"""),
            Base64Url("""{"k":"my-matches","v":["2026-01-01T00:00:00+00:00","não-é-guid"]}"""),
            Base64Url("""{"k":"my-matches","v":[1,2]}"""),
            new string('A', 20_000),
        ];

        foreach (var cursor in cursors)
        {
            foreach (var role in new[] { "organizer", "participant" })
            {
                using var response = await organizer.Client.GetAsync(new Uri($"{MyMatchesPath}?role={role}&cursor={Uri.EscapeDataString(cursor)}", UriKind.Relative), Ct);
                var raw = await response.Content.ReadAsStringAsync(Ct);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"cursor '{cursor[..Math.Min(cursor.Length, 60)]}': {(int)response.StatusCode} {raw}");
                Assert.Contains("urn:applada:problem:invalid-cursor", raw, StringComparison.Ordinal);
            }
        }

        // Data com offset diferente de zero é válida: vai para UTC antes do banco (o Npgsql recusaria), nunca 500.
        var withOffset = Base64Url("""{"k":"my-matches","v":["2000-01-01T00:00:00+03:00","00000000-0000-0000-0000-000000000000"]}""");
        foreach (var role in new[] { "organizer", "participant" })
        {
            using var response = await first.Client.GetAsync(new Uri($"{MyMatchesPath}?role={role}&cursor={withOffset}", UriKind.Relative), Ct);
            var raw = await response.Content.ReadAsStringAsync(Ct);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role}: {(int)response.StatusCode} {raw}");
        }

        var page = (await WalkAsync(first.Client, $"role=participant&cursor={withOffset}", limit: 10)).Single();
        Assert.Equal(matchId, Assert.Single(page.Items).Id);
    }

    // ---------- Apoio ----------

    /// <summary>Percorre as páginas seguindo <c>nextCursor</c> (no máximo 20) e devolve todas.</summary>
    private static async Task<List<Page>> WalkAsync(HttpClient client, string query, int limit)
    {
        var pages = new List<Page>();
        string? cursor = null;
        do
        {
            var url = $"{MyMatchesPath}?{query}&limit={limit}{(cursor is null ? string.Empty : $"&cursor={cursor}")}";
            using var response = await client.GetAsync(new Uri(url, UriKind.Relative), Ct);
            var raw = await response.Content.ReadAsStringAsync(Ct);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {raw}");
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
            using var body = JsonDocument.Parse(raw);
            var items = body.RootElement.GetProperty("items").EnumerateArray().Select(ReadItem).ToArray();
            var next = body.RootElement.GetProperty("nextCursor");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
            pages.Add(new Page(items, cursor));
        }
        while (cursor is not null && pages.Count < 20 && !query.Contains("cursor=", StringComparison.Ordinal));

        return pages;
    }

    private static Item ReadItem(JsonElement item)
    {
        Assert.Equal(7, item.EnumerateObject().Count());
        var myStatus = item.GetProperty("myStatus");
        return new Item(
            item.GetProperty("id").GetGuid(),
            item.GetProperty("title").GetString()!,
            item.GetProperty("startsAt").GetDateTimeOffset(),
            item.GetProperty("status").GetString()!,
            item.GetProperty("confirmedCount").GetInt32(),
            item.GetProperty("capacity").GetInt32(),
            myStatus.ValueKind == JsonValueKind.Null ? null : myStatus.GetString());
    }

    private static string Base64Url(string json) => System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

    private sealed record Item(Guid Id, string Title, DateTimeOffset StartsAt, string Status, int ConfirmedCount, int Capacity, string? MyStatus);

    private sealed record Page(IReadOnlyList<Item> Items, string? NextCursor);
}
