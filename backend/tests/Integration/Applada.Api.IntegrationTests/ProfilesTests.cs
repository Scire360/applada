using System.Net;
using System.Net.Http.Json;
using System.Text;
using Applada.Api.IntegrationTests.Infrastructure;
using static Applada.Api.IntegrationTests.Infrastructure.MatchesApi;
using static Applada.Api.IntegrationTests.Infrastructure.ProblemAssertions;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class ProfilesTests(ApiFactory factory)
{
    private const string ProfilePath = "/api/v1/me/profile";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Own_display_name_is_normalized_and_persists_across_requests()
    {
        var subject = NewSubject("profile");
        using var client = factory.CreateClientFor(subject);
        Assert.Null(await ReadNameAsync(await client.GetAsync(ProfilePath, Ct)));

        Assert.Equal("João da Silva", await ReadNameAsync(
            await client.PutAsJsonAsync(ProfilePath, new { displayName = "  João da Silva  " }, Ct)));

        using var anotherSession = factory.CreateClientFor(subject);
        Assert.Equal("João da Silva", await ReadNameAsync(await anotherSession.GetAsync(ProfilePath, Ct)));
        Assert.Equal("João", await ReadNameAsync(
            await anotherSession.PutAsJsonAsync(ProfilePath, new { displayName = "João" }, Ct)));
        Assert.Equal("João", await ReadNameAsync(await client.GetAsync(ProfilePath, Ct)));

        using var anotherUser = factory.CreateClientFor(NewSubject("profile"));
        Assert.Null(await ReadNameAsync(await anotherUser.GetAsync(ProfilePath, Ct)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("João\tSilva")]
    [InlineData("123456789012345678901234567890123456789012345678901234567890123456789012345678901")]
    public async Task Invalid_display_name_returns_validation_error(string? displayName)
    {
        using var client = factory.CreateClientFor(NewSubject("profile"));
        using var response = await client.PutAsJsonAsync(ProfilePath, new { displayName }, Ct);

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("Dados inválidos.", problem.RootElement.GetProperty("title").GetString());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("displayName", out _));
        Assert.Null(await ReadNameAsync(await client.GetAsync(ProfilePath, Ct)));
    }

    [Fact]
    public async Task Unsupported_content_type_returns_415()
    {
        using var client = factory.CreateClientFor(NewSubject("profile"));
        using var content = new StringContent("""{"displayName":"João"}""", Encoding.UTF8, "text/plain");
        using var response = await client.PutAsync(ProfilePath, content, Ct);
        using var problem = await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "unsupported-media-type");
    }

    [Theory]
    [InlineData(false, 8192)]
    [InlineData(true, 8192)]
    [InlineData(false, 8193)]
    [InlineData(true, 8193)]
    public async Task Update_limits_body_to_8kb(bool chunked, int size)
    {
        var bytes = Encoding.UTF8.GetBytes("""{"displayName":"Ana"}""".PadRight(size));
        using var client = factory.CreateClientFor(NewSubject("profile"));
        using var request = new HttpRequestMessage(HttpMethod.Put, ProfilePath)
        {
            Content = chunked ? new StreamContent(new UnknownLengthStream(bytes)) : new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.TransferEncodingChunked = chunked;
        Assert.Equal(chunked ? (long?)null : size, request.Content.Headers.ContentLength);

        using var response = await client.SendAsync(request, Ct);
        if (size == 8192)
        {
            Assert.Equal("Ana", await ReadNameAsync(response));
        }
        else
        {
            using var problem = await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "payload-too-large");
            Assert.Null(await ReadNameAsync(await client.GetAsync(ProfilePath, Ct)));
        }
    }

    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    [Fact]
    public async Task Profile_requires_authentication_for_reads_and_writes()
    {
        using var client = factory.CreateClient();
        using var read = await client.GetAsync(ProfilePath, Ct);
        await AssertProblemAsync(read, HttpStatusCode.Unauthorized, "unauthorized");

        using var write = await client.PutAsJsonAsync(ProfilePath, new { displayName = "João" }, Ct);
        await AssertProblemAsync(write, HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Parallel_initial_writes_can_both_persist_the_same_name()
    {
        using var client = factory.CreateClientFor(NewSubject("profile"));
        var writes = await Task.WhenAll(
            client.PutAsJsonAsync(ProfilePath, new { displayName = "João" }, Ct),
            client.PutAsJsonAsync(ProfilePath, new { displayName = "João" }, Ct));

        foreach (var write in writes)
        {
            Assert.Equal("João", await ReadNameAsync(write));
        }

        Assert.Equal("João", await ReadNameAsync(await client.GetAsync(ProfilePath, Ct)));
    }

    [Fact]
    public async Task Authorized_participant_page_includes_names_and_null_for_missing_profile()
    {
        using var organizer = await NewUserAsync(factory, "profile");
        using var named = await NewUserAsync(factory, "profile");
        using var unnamed = await NewUserAsync(factory, "profile");
        Assert.Equal("Jogadora da fila", await ReadNameAsync(
            await named.Client.PutAsJsonAsync(ProfilePath, new { displayName = "Jogadora da fila" }, Ct)));

        var matchId = await CreateMatchAsync(organizer.Client, factory.Clock.GetUtcNow().AddDays(2), capacity: 1);
        await RegisterExpectingAsync(unnamed.Client, matchId, HttpStatusCode.Created);
        await RegisterExpectingAsync(named.Client, matchId, HttpStatusCode.Created);

        var route = new Uri($"{MatchesPath}/{matchId}/registrations", UriKind.Relative);
        using var page = await named.Client.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var body = await ReadJsonAsync(page);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Collection(items,
            first =>
            {
                Assert.Equal(unnamed.Id, first.GetProperty("userId").GetGuid());
                Assert.Null(first.GetProperty("displayName").GetString());
            },
            second =>
            {
                Assert.Equal(named.Id, second.GetProperty("userId").GetGuid());
                Assert.Equal("Jogadora da fila", second.GetProperty("displayName").GetString());
            });

        using var withdrawal = await WithdrawAsync(named.Client, matchId);
        Assert.Equal(HttpStatusCode.NoContent, withdrawal.StatusCode);
        using var denied = await named.Client.GetAsync(route, Ct);
        using var problem = await AssertProblemAsync(denied, HttpStatusCode.Forbidden, "participants-forbidden");
        Assert.False(problem.RootElement.TryGetProperty("items", out _));
    }

    private static async Task<string?> ReadNameAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var body = await ReadJsonAsync(response);
            return body.RootElement.GetProperty("displayName").GetString();
        }
    }
}
