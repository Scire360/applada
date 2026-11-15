using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class RateLimitingTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Writes_over_limit_return_429()
    {
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Matches:WriteRateLimitPerMinute", "3"));
        using var client = ClientFor(limited, $"rate-{Guid.NewGuid():N}");
        using var other = ClientFor(limited, $"rate-{Guid.NewGuid():N}");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var response = await PostMatchAsync(client);
            statuses.Add(response.StatusCode);
            if (i == 3)
            {
                var raw = await response.Content.ReadAsStringAsync(Ct);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                using var problem = JsonDocument.Parse(raw);
                Assert.Equal("urn:applada:problem:rate-limited", problem.RootElement.GetProperty("type").GetString());
                Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
            }
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);

        // O limite é por usuário: outro usuário ainda escreve; e leituras não entram na conta.
        using var fromOther = await PostMatchAsync(other);
        Assert.Equal(HttpStatusCode.Created, fromOther.StatusCode);
        using var read = await client.GetAsync("/api/v1/me", Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task Registration_writes_count_against_the_limit_and_reads_do_not()
    {
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Matches:WriteRateLimitPerMinute", "3"));
        using var organizer = ClientFor(limited, $"rate-{Guid.NewGuid():N}");
        using var player = ClientFor(limited, $"rate-{Guid.NewGuid():N}");
        using var created = await PostMatchAsync(organizer);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var matchId = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("id").GetGuid();
        var registrations = $"/api/v1/matches/{matchId}/registrations";

        var statuses = new List<HttpStatusCode>();
        foreach (var write in new Func<Task<HttpResponseMessage>>[]
        {
            () => player.PostAsync(registrations, content: null, Ct),
            () => player.DeleteAsync($"{registrations}/me", Ct),
            () => player.PostAsync(registrations, content: null, Ct),
            () => player.DeleteAsync($"{registrations}/me", Ct),
        })
        {
            using var response = await write();
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);

        // Leituras não entram na conta.
        using var participants = await player.GetAsync(registrations, Ct);
        Assert.Equal(HttpStatusCode.OK, participants.StatusCode);
        using var mine = await player.GetAsync("/api/v1/me/matches?role=participant", Ct);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
    }

    private HttpClient ClientFor(WebApplicationFactory<Program> host, string subject)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Issue(subject, now: factory.Clock.GetUtcNow()));
        return client;
    }

    private async Task<HttpResponseMessage> PostMatchAsync(HttpClient client)
    {
        var startsAt = factory.Clock.GetUtcNow().AddDays(2).ToString("O", CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/matches")
        {
            Content = new StringContent(
                $$"""{"title":"Pelada","startsAt":"{{startsAt}}","timeZone":"America/Sao_Paulo","durationMinutes":60,"capacity":10,"visibility":"public"}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        return await client.SendAsync(request, Ct);
    }
}
