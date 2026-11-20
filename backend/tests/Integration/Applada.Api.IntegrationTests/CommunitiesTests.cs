using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Communities.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Applada.Api.IntegrationTests.Infrastructure.MatchesApi;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class CommunitiesTests(ApiFactory factory)
{
    private const string GroupsPath = "/api/v1/groups";
    private const string MyGroupsPath = "/api/v1/me/groups";
    private const string AcceptancePath = "/api/v1/group-invitations/acceptance";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creator_becomes_owner_and_sees_group_in_my_groups()
    {
        using var owner = await NewUserAsync(factory, "grp");

        using var created = await owner.Client.PostAsJsonAsync(GroupsPath, new { name = "  Pelada de quinta  ", description = "Society no bairro" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        using var body = await ReadJsonAsync(created);
        var group = body.RootElement;
        var groupId = group.GetProperty("id").GetGuid();
        Assert.Equal("Pelada de quinta", group.GetProperty("name").GetString());
        Assert.Equal("Society no bairro", group.GetProperty("description").GetString());
        Assert.Equal("owner", group.GetProperty("myRole").GetString());
        Assert.Equal(1, group.GetProperty("memberCount").GetInt32());

        using var read = await owner.Client.GetAsync(new Uri($"{GroupsPath}/{groupId}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using var mine = await owner.Client.GetAsync(new Uri(MyGroupsPath, UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        using var page = await ReadJsonAsync(mine);
        var item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(groupId, item.GetProperty("id").GetGuid());
        Assert.Equal("owner", item.GetProperty("myRole").GetString());

        var joined = await OutboxAsync(groupId);
        var evt = Assert.Single(joined);
        Assert.Equal(owner.Id, evt.GetProperty("userId").GetGuid());
        Assert.Equal("owner", evt.GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("Nome\tcom tab")]
    [InlineData("123456789012345678901234567890123456789012345678901234567890123456789012345678901")]
    public async Task Invalid_group_name_is_rejected(string? name)
    {
        using var user = await NewUserAsync(factory, "grp");
        using var response = await user.Client.PostAsJsonAsync(GroupsPath, new { name }, Ct);
        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Oversized_body_is_rejected_even_without_content_length()
    {
        using var user = await NewUserAsync(factory, "grp");
        var json = $$"""{"name":"Grupo","description":"{{new string('a', 20_000)}}"}""";

        // StreamContent sem tamanho conhecido: o HttpClient envia Transfer-Encoding: chunked, sem Content-Length.
        using var content = new StreamContent(new UnknownLengthStream(Encoding.UTF8.GetBytes(json)));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var response = await user.Client.PostAsync(new Uri(GroupsPath, UriKind.Relative), content, Ct);

        using (await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "payload-too-large"))
        {
        }
    }

    [Fact]
    public async Task Non_member_gets_404_on_every_group_route_and_member_gets_403_on_admin_routes()
    {
        using var owner = await NewUserAsync(factory, "grp");
        using var member = await NewUserAsync(factory, "grp");
        using var stranger = await NewUserAsync(factory, "grp");
        var groupId = await CreateGroupAsync(owner.Client);
        await JoinAsync(owner.Client, member.Client, groupId);

        foreach (var response in new[]
        {
            await stranger.Client.GetAsync(new Uri($"{GroupsPath}/{groupId}", UriKind.Relative), Ct),
            await stranger.Client.GetAsync(new Uri($"{GroupsPath}/{groupId}/members", UriKind.Relative), Ct),
            await stranger.Client.PostAsJsonAsync($"{GroupsPath}/{groupId}/invitations", new { }, Ct),
            await stranger.Client.DeleteAsync(new Uri($"{GroupsPath}/{groupId}/members/{member.Id}", UriKind.Relative), Ct),
            await stranger.Client.GetAsync(new Uri($"{GroupsPath}/{Guid.NewGuid()}", UriKind.Relative), Ct),
        })
        {
            using (response)
            using (await AssertProblemAsync(response, HttpStatusCode.NotFound, "group-not-found"))
            {
            }
        }

        using var invite = await member.Client.PostAsJsonAsync($"{GroupsPath}/{groupId}/invitations", new { }, Ct);
        using (await AssertProblemAsync(invite, HttpStatusCode.Forbidden, "group-forbidden"))
        {
        }

        using var promote = await member.Client.PatchAsJsonAsync($"{GroupsPath}/{groupId}/members/{owner.Id}", new { role = "member" }, Ct);
        using (await AssertProblemAsync(promote, HttpStatusCode.Forbidden, "group-forbidden"))
        {
        }

        using var kick = await member.Client.DeleteAsync(new Uri($"{GroupsPath}/{groupId}/members/{owner.Id}", UriKind.Relative), Ct);
        using (await AssertProblemAsync(kick, HttpStatusCode.Forbidden, "group-forbidden"))
        {
        }
    }

    [Fact]
    public async Task Invitation_flow_lists_members_with_names_and_repeated_accept_does_not_consume_use()
    {
        using var owner = await NewUserAsync(factory, "grp");
        using var guest = await NewUserAsync(factory, "grp");
        await guest.Client.PutAsJsonAsync("/api/v1/me/profile", new { displayName = "Convidado" }, Ct);
        var groupId = await CreateGroupAsync(owner.Client);
        var token = await InviteAsync(owner.Client, groupId, maxUses: 2);

        using var first = await AcceptAsync(guest.Client, token);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var again = await AcceptAsync(guest.Client, token);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        Assert.Equal(1, await UseCountAsync(token));

        using var members = await guest.Client.GetAsync(new Uri($"{GroupsPath}/{groupId}/members", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        using var page = await ReadJsonAsync(members);
        var items = page.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Collection(items,
            first => Assert.Equal("owner", first.GetProperty("role").GetString()),
            second =>
            {
                Assert.Equal(guest.Id, second.GetProperty("userId").GetGuid());
                Assert.Equal("Convidado", second.GetProperty("displayName").GetString());
                Assert.Equal("member", second.GetProperty("role").GetString());
            });

        Assert.Equal(2, (await OutboxAsync(groupId)).Count);
    }

    [Fact]
    public async Task Invalid_expired_or_revoked_invitations_all_return_the_same_404()
    {
        using var owner = await NewUserAsync(factory, "grp");
        using var guest = await NewUserAsync(factory, "grp");
        var groupId = await CreateGroupAsync(owner.Client);

        using (var bogus = await AcceptAsync(guest.Client, "nao-existe"))
        using (await AssertProblemAsync(bogus, HttpStatusCode.NotFound, "invitation-invalid"))
        {
        }

        using var created = await owner.Client.PostAsJsonAsync($"{GroupsPath}/{groupId}/invitations", new { }, Ct);
        using var body = await ReadJsonAsync(created);
        var invitationId = body.RootElement.GetProperty("id").GetGuid();
        var revokedToken = body.RootElement.GetProperty("token").GetString()!;
        using var revoke = await owner.Client.DeleteAsync(new Uri($"{GroupsPath}/{groupId}/invitations/{invitationId}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var revokeAgain = await owner.Client.DeleteAsync(new Uri($"{GroupsPath}/{groupId}/invitations/{invitationId}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, revokeAgain.StatusCode);
        using (var revoked = await AcceptAsync(guest.Client, revokedToken))
        using (await AssertProblemAsync(revoked, HttpStatusCode.NotFound, "invitation-invalid"))
        {
        }

        // Expira pelo banco, não pelo relógio: adiantar o relógio compartilhado deixaria eventos "do futuro" no outbox
        // do Identity e atrapalharia os testes de outbox.
        var expiringToken = await InviteAsync(owner.Client, groupId, maxUses: 5, expiresInHours: 1);
        await ExpireInvitationAsync(expiringToken);
        using (var expired = await AcceptAsync(guest.Client, expiringToken))
        using (await AssertProblemAsync(expired, HttpStatusCode.NotFound, "invitation-invalid"))
        {
        }

        Assert.Equal(1, await MemberCountAsync(groupId));
    }

    [Fact]
    public async Task Concurrent_accepts_never_exceed_max_uses()
    {
        using var owner = await NewUserAsync(factory, "grp");
        var groupId = await CreateGroupAsync(owner.Client);
        var token = await InviteAsync(owner.Client, groupId, maxUses: 3);
        var guests = await NewUsersAsync(factory, 12, "grp");
        try
        {
            var responses = await Task.WhenAll(guests.Select(g => AcceptAsync(g.Client, token)));
            Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.NotFound));
            foreach (var response in responses)
            {
                response.Dispose();
            }

            Assert.Equal(4, await MemberCountAsync(groupId));
            Assert.Equal(4, await MembershipRowsAsync(groupId));
            Assert.Equal(3, await UseCountAsync(token));
        }
        finally
        {
            foreach (var guest in guests)
            {
                guest.Dispose();
            }
        }
    }

    [Fact]
    public async Task Group_member_limit_is_enforced_under_concurrency()
    {
        using var owner = await NewUserAsync(factory, "grp");
        var groupId = await CreateGroupAsync(owner.Client);
        var token = await InviteAsync(owner.Client, groupId, maxUses: 500);
        await SetMemberCountAsync(groupId, 498);

        var guests = await NewUsersAsync(factory, 6, "grp");
        try
        {
            var responses = await Task.WhenAll(guests.Select(g => AcceptAsync(g.Client, token)));
            Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            var rejected = responses.Where(r => r.StatusCode != HttpStatusCode.Created).ToArray();
            Assert.Equal(4, rejected.Length);
            foreach (var response in rejected)
            {
                using (await AssertProblemAsync(response, HttpStatusCode.Conflict, "group-full"))
                {
                }
            }

            Assert.Equal(500, await MemberCountAsync(groupId));
            Assert.Equal(2, await UseCountAsync(token));
        }
        finally
        {
            foreach (var guest in guests)
            {
                guest.Dispose();
            }
        }
    }

    [Fact]
    public async Task Roles_and_removal_follow_the_hierarchy()
    {
        using var owner = await NewUserAsync(factory, "grp");
        using var admin = await NewUserAsync(factory, "grp");
        using var member = await NewUserAsync(factory, "grp");
        using var other = await NewUserAsync(factory, "grp");
        var groupId = await CreateGroupAsync(owner.Client);
        await JoinAsync(owner.Client, admin.Client, groupId);
        await JoinAsync(owner.Client, member.Client, groupId);
        await JoinAsync(owner.Client, other.Client, groupId);

        using var promote = await owner.Client.PatchAsJsonAsync($"{GroupsPath}/{groupId}/members/{admin.Id}", new { role = "admin" }, Ct);
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

        using (var badRole = await owner.Client.PatchAsJsonAsync($"{GroupsPath}/{groupId}/members/{admin.Id}", new { role = "owner" }, Ct))
        using (await AssertProblemAsync(badRole, HttpStatusCode.BadRequest, "validation"))
        {
        }

        using (var ownerRole = await owner.Client.PatchAsJsonAsync($"{GroupsPath}/{groupId}/members/{owner.Id}", new { role = "member" }, Ct))
        using (await AssertProblemAsync(ownerRole, HttpStatusCode.Conflict, "owner-role-fixed"))
        {
        }

        // Admin convida e remove membro comum, mas não mexe em papéis nem remove o owner.
        await InviteAsync(admin.Client, groupId, maxUses: 1);
        using var kick = await admin.Client.DeleteAsync(new Uri($"{GroupsPath}/{groupId}/members/{member.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, kick.StatusCode);
        using (var kickOwner = await admin.Client.DeleteAsync(new Uri($"{GroupsPath}/{groupId}/members/{owner.Id}", UriKind.Relative), Ct))
        using (await AssertProblemAsync(kickOwner, HttpStatusCode.Forbidden, "group-forbidden"))
        {
        }

        // Quem foi removido perde o acesso.
        using (var lost = await member.Client.GetAsync(new Uri($"{GroupsPath}/{groupId}", UriKind.Relative), Ct))
        using (await AssertProblemAsync(lost, HttpStatusCode.NotFound, "group-not-found"))
        {
        }

        // Membro sai sozinho; owner não pode sair.
        using var leave = await other.Client.DeleteAsync(new Uri($"{GroupsPath}/{groupId}/members/{other.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        using (var ownerLeave = await owner.Client.DeleteAsync(new Uri($"{GroupsPath}/{groupId}/members/{owner.Id}", UriKind.Relative), Ct))
        using (await AssertProblemAsync(ownerLeave, HttpStatusCode.Conflict, "owner-cannot-leave"))
        {
        }

        Assert.Equal(2, await MemberCountAsync(groupId));
        Assert.Equal(2, await MembershipRowsAsync(groupId));
    }

    [Fact]
    public async Task Match_with_group_requires_organizer_role_in_that_group()
    {
        using var owner = await NewUserAsync(factory, "grp");
        using var member = await NewUserAsync(factory, "grp");
        using var stranger = await NewUserAsync(factory, "grp");
        var groupId = await CreateGroupAsync(owner.Client);
        await JoinAsync(owner.Client, member.Client, groupId);

        using (var byOwner = await CreateGroupMatchAsync(owner.Client, groupId))
        {
            Assert.Equal(HttpStatusCode.Created, byOwner.StatusCode);
            using var body = await ReadJsonAsync(byOwner);
            Assert.Equal(groupId, body.RootElement.GetProperty("groupId").GetGuid());
        }

        foreach (var (client, group) in new[] { (member.Client, groupId), (stranger.Client, groupId), (owner.Client, Guid.NewGuid()) })
        {
            using var denied = await CreateGroupMatchAsync(client, group);
            using (await AssertProblemAsync(denied, HttpStatusCode.UnprocessableEntity, "group-not-allowed"))
            {
            }
        }
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(GroupsPath, new { name = "Grupo de teste" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> InviteAsync(HttpClient client, Guid groupId, int maxUses, int? expiresInHours = null)
    {
        using var response = await client.PostAsJsonAsync($"{GroupsPath}/{groupId}/invitations", new { maxUses, expiresInHours }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(maxUses, body.RootElement.GetProperty("maxUses").GetInt32());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync(AcceptancePath, new { token }, Ct);

    private static async Task JoinAsync(HttpClient owner, HttpClient guest, Guid groupId)
    {
        var token = await InviteAsync(owner, groupId, maxUses: 1);
        using var response = await AcceptAsync(guest, token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static Task<HttpResponseMessage> CreateGroupMatchAsync(HttpClient client, Guid groupId)
    {
        var start = DateTimeOffset.UtcNow.AddDays(3).ToString("O", CultureInfo.InvariantCulture);
        var request = new HttpRequestMessage(HttpMethod.Post, MatchesPath)
        {
            Content = new StringContent(
                $$"""{"title":"Pelada do grupo","startsAt":"{{start}}","timeZone":"America/Sao_Paulo","durationMinutes":60,"capacity":10,"visibility":"public","groupId":"{{groupId}}"}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        return client.SendAsync(request, Ct);
    }

    private async Task<IReadOnlyList<JsonElement>> OutboxAsync(Guid groupId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunitiesDbContext>();
        var payloads = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.AggregateId == groupId && m.EventType == "communities.group-member-joined.v1")
            .OrderBy(m => m.OccurredAt)
            .Select(m => m.Payload)
            .ToListAsync(Ct);
        return [.. payloads.Select(p => JsonDocument.Parse(p).RootElement.Clone())];
    }

    private Task<int> MemberCountAsync(Guid groupId) =>
        ScalarAsync($"SELECT member_count AS \"Value\" FROM communities.groups WHERE id = '{groupId}'");

    private Task<int> MembershipRowsAsync(Guid groupId) =>
        ScalarAsync($"SELECT count(*)::int AS \"Value\" FROM communities.memberships WHERE group_id = '{groupId}'");

    private Task<int> UseCountAsync(string token)
    {
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return ScalarAsync($"SELECT use_count AS \"Value\" FROM communities.invitations WHERE token_hash = decode('{hash}', 'hex')");
    }

    private async Task ExpireInvitationAsync(string token)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var past = factory.Clock.GetUtcNow().AddMinutes(-1);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunitiesDbContext>();
        await db.Database.ExecuteSqlAsync($"UPDATE communities.invitations SET expires_at = {past} WHERE token_hash = {hash}", Ct);
    }

    private async Task SetMemberCountAsync(Guid groupId, int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunitiesDbContext>();
        await db.Database.ExecuteSqlAsync($"UPDATE communities.groups SET member_count = {count} WHERE id = {groupId}", Ct);
    }

    /// <summary>Stream que não informa tamanho, para forçar envio chunked.</summary>
    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    private async Task<int> ScalarAsync(string sql)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunitiesDbContext>();
#pragma warning disable EF1002 // SQL só de teste, com valores gerados pelo próprio teste (GUID e hex).
        return await db.Database.SqlQueryRaw<int>(sql).SingleAsync(Ct);
#pragma warning restore EF1002
    }
}
