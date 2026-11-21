using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using static Applada.Api.IntegrationTests.Infrastructure.MatchesApi;
using static Applada.Api.IntegrationTests.Infrastructure.ProblemAssertions;

namespace Applada.Api.IntegrationTests;

/// <summary><c>GET /groups/{id}/matches</c>: próximas partidas agendadas do grupo, visíveis só aos membros, por cursor.</summary>
[Collection("api")]
public class GroupMatchesTests(ApiFactory factory)
{
    private const string GroupsPath = "/api/v1/groups";
    private const string AcceptancePath = "/api/v1/group-invitations/acceptance";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Member_lists_upcoming_scheduled_matches_of_the_group_in_start_order()
    {
        using var owner = await NewUserAsync(factory, "gm");
        using var member = await NewUserAsync(factory, "gm");
        var groupId = await CreateGroupAsync(owner.Client);
        await JoinAsync(owner.Client, member.Client, groupId);
        var now = factory.Clock.GetUtcNow();
        var plus2 = await CreateGroupMatchAsync(owner.Client, groupId, now.AddDays(2));
        var plus1 = await CreateGroupMatchAsync(owner.Client, groupId, now.AddDays(1));
        var plus3 = await CreateGroupMatchAsync(owner.Client, groupId, now.AddDays(3));
        await CreateMatchAsync(owner.Client, now.AddDays(1), capacity: 4); // sem grupo

        var items = (await WalkAsync(member.Client, groupId, limit: 20)).SelectMany(p => p.Items).ToArray();

        Assert.Equal([plus1, plus2, plus3], items.Select(i => i.Id));
        Assert.All(items, i => Assert.Null(i.MyStatus));
        Assert.All(items, i => Assert.Equal("scheduled", i.Status));
    }

    [Fact]
    public async Task Cancelled_past_and_other_group_matches_are_not_listed()
    {
        using var owner = await NewUserAsync(factory, "gm");
        using var member = await NewUserAsync(factory, "gm");
        var groupId = await CreateGroupAsync(owner.Client);
        var otherGroupId = await CreateGroupAsync(owner.Client);
        await JoinAsync(owner.Client, member.Client, groupId);
        var now = factory.Clock.GetUtcNow();
        var kept = await CreateGroupMatchAsync(owner.Client, groupId, now.AddDays(2));
        var cancelled = await CreateGroupMatchAsync(owner.Client, groupId, now.AddDays(1));
        await CreateGroupMatchAsync(owner.Client, otherGroupId, now.AddDays(1));
        await CreateGroupMatchAsync(owner.Client, groupId, now.AddMinutes(10)); // vira passado
        using (var cancel = await owner.Client.PostAsync(new Uri($"{MatchesPath}/{cancelled}/cancellation", UriKind.Relative), null, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        }

        var delta = TimeSpan.FromMinutes(20);
        factory.Clock.Advance(delta);
        try
        {
            var items = (await WalkAsync(member.Client, groupId, limit: 20)).SelectMany(p => p.Items).ToArray();

            Assert.Equal([kept], items.Select(i => i.Id));
        }
        finally
        {
            factory.Clock.Advance(-delta);
        }
    }

    [Fact]
    public async Task Non_member_and_unknown_group_get_404_group_not_found()
    {
        using var owner = await NewUserAsync(factory, "gm");
        using var stranger = await NewUserAsync(factory, "gm");
        var groupId = await CreateGroupAsync(owner.Client);
        await CreateGroupMatchAsync(owner.Client, groupId, factory.Clock.GetUtcNow().AddDays(1));

        foreach (var id in new[] { groupId, Guid.NewGuid() })
        {
            using var response = await stranger.Client.GetAsync(new Uri($"{GroupsPath}/{id}/matches", UriKind.Relative), Ct);
            using (await AssertProblemAsync(response, HttpStatusCode.NotFound, "group-not-found"))
            {
            }
        }
    }

    [Fact]
    public async Task Without_token_returns_401()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"{GroupsPath}/{Guid.NewGuid()}/matches", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pages_with_cursor_without_repeating_items()
    {
        using var owner = await NewUserAsync(factory, "gm");
        var groupId = await CreateGroupAsync(owner.Client);
        var now = factory.Clock.GetUtcNow();
        var first = await CreateGroupMatchAsync(owner.Client, groupId, now.AddDays(1));
        var second = await CreateGroupMatchAsync(owner.Client, groupId, now.AddDays(2));
        var third = await CreateGroupMatchAsync(owner.Client, groupId, now.AddDays(3));

        var pages = await WalkAsync(owner.Client, groupId, limit: 2);

        Assert.Equal([2, 1], pages.Select(p => p.Items.Count));
        Assert.NotNull(pages[0].NextCursor);
        Assert.Null(pages[1].NextCursor);
        Assert.Equal([first, second, third], pages.SelectMany(p => p.Items).Select(i => i.Id));

        // Cursor de "minhas partidas" é de outra listagem.
        using var mine = await owner.Client.GetAsync(new Uri($"{MyMatchesPath}?role=organizer&limit=1", UriKind.Relative), Ct);
        using var mineBody = await ReadJsonAsync(mine);
        var myCursor = mineBody.RootElement.GetProperty("nextCursor").GetString();
        Assert.NotNull(myCursor);
        using var response = await owner.Client.GetAsync(new Uri($"{GroupsPath}/{groupId}/matches?cursor={myCursor}", UriKind.Relative), Ct);
        using (await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid-cursor"))
        {
        }
    }

    [Fact]
    public async Task Invalid_cursor_returns_400()
    {
        using var owner = await NewUserAsync(factory, "gm");
        var groupId = await CreateGroupAsync(owner.Client);
        string[] cursors =
        [
            CursorSamples.WithInvalidUtf8Key("group-matches"),
            Base64Url.EncodeToString("""{"k":"group-matches","v":["\ud800","00000000-0000-0000-0000-000000000001"]}"""u8),
            Base64Url.EncodeToString("""{"k":"group-matches","v":["2026-01-01T00:00:00+00:00","\ud800"]}"""u8),
        ];

        foreach (var cursor in cursors)
        {
            using var response = await owner.Client.GetAsync(new Uri($"{GroupsPath}/{groupId}/matches?cursor={cursor}", UriKind.Relative), Ct);
            using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid-cursor");
        }
    }

    [Fact]
    public async Task Response_is_private_no_store()
    {
        using var owner = await NewUserAsync(factory, "gm");
        var groupId = await CreateGroupAsync(owner.Client);

        using var response = await owner.Client.GetAsync(new Uri($"{GroupsPath}/{groupId}/matches", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    // ---------- Apoio ----------

    private static async Task<List<Page>> WalkAsync(HttpClient client, Guid groupId, int limit)
    {
        var pages = new List<Page>();
        string? cursor = null;
        do
        {
            var url = $"{GroupsPath}/{groupId}/matches?limit={limit}{(cursor is null ? string.Empty : $"&cursor={cursor}")}";
            using var response = await client.GetAsync(new Uri(url, UriKind.Relative), Ct);
            var raw = await response.Content.ReadAsStringAsync(Ct);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {raw}");
            using var body = JsonDocument.Parse(raw);
            var items = body.RootElement.GetProperty("items").EnumerateArray().Select(ReadItem).ToArray();
            var next = body.RootElement.GetProperty("nextCursor");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
            pages.Add(new Page(items, cursor));
        }
        while (cursor is not null && pages.Count < 20);

        return pages;
    }

    private static Item ReadItem(JsonElement item)
    {
        var myStatus = item.GetProperty("myStatus");
        return new Item(
            item.GetProperty("id").GetGuid(),
            item.GetProperty("status").GetString()!,
            myStatus.ValueKind == JsonValueKind.Null ? null : myStatus.GetString());
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(GroupsPath, new { name = "Grupo de teste" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task JoinAsync(HttpClient owner, HttpClient guest, Guid groupId)
    {
        using var invite = await owner.PostAsJsonAsync($"{GroupsPath}/{groupId}/invitations", new { maxUses = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        using var inviteBody = await ReadJsonAsync(invite);
        var token = inviteBody.RootElement.GetProperty("token").GetString()!;
        using var accepted = await guest.PostAsJsonAsync(AcceptancePath, new { token }, Ct);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    private static async Task<Guid> CreateGroupMatchAsync(HttpClient organizer, Guid groupId, DateTimeOffset startsAt)
    {
        var start = startsAt.ToString("O", CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, MatchesPath)
        {
            Content = new StringContent(
                $$"""{"title":"Pelada do grupo","startsAt":"{{start}}","timeZone":"America/Sao_Paulo","durationMinutes":60,"capacity":10,"visibility":"public","groupId":"{{groupId}}"}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        using var response = await organizer.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private sealed record Item(Guid Id, string Status, string? MyStatus);

    private sealed record Page(IReadOnlyList<Item> Items, string? NextCursor);
}
