using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Discovery.Infrastructure;
using Applada.Modules.Matches.Contracts;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class DiscoverMatchesTests(ApiFactory factory)
{
    private const string Path = "/api/v1/discover/matches";
    private const double Latitude = -20.47;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private HttpClient NewClient() => factory.CreateClientFor(MatchesApi.NewSubject("discover"));

    [Fact]
    public async Task Finds_match_3km_away_not_30km_with_radius_10()
    {
        using var client = NewClient();
        var near = await CreateVenueAsync(client, -54.62, 3);
        var far = await CreateVenueAsync(client, -54.62, 30);
        var match = await AddMatchAsync(near, 1, full: true);
        await AddMatchAsync(far, 1);

        var item = Assert.Single((await PageAsync(client, -54.62)).GetProperty("items").EnumerateArray());
        Assert.Equal(match.Id, item.GetProperty("matchId").GetGuid());
        Assert.Equal(match.Title, item.GetProperty("title").GetString());
        Assert.Equal(match.StartsAt, item.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(match.TimeZone, item.GetProperty("timeZone").GetString());
        Assert.Equal(1, item.GetProperty("capacity").GetInt32());
        Assert.Equal(1, item.GetProperty("confirmedCount").GetInt32());
        Assert.Equal(0, item.GetProperty("spotsLeft").GetInt32());
        var venue = item.GetProperty("venue");
        Assert.Equal(near, venue.GetProperty("id").GetGuid());
        Assert.Equal("Campo público", venue.GetProperty("name").GetString());
        Assert.Equal("Praça central", venue.GetProperty("addressText").GetString());
    }

    [Fact]
    public async Task Radius_edge_9_9km_in_10_1km_out()
    {
        using var client = NewClient();
        var inside = await AddMatchAsync(await CreateVenueAsync(client, -56, 9.9), 1);
        await AddMatchAsync(await CreateVenueAsync(client, -56, 10.1), 1);

        var page = await PageAsync(client, -56, "&radiusKm=10");
        Assert.Equal(inside.Id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("matchId").GetGuid());
    }

    [Fact]
    public async Task Excludes_cancelled_and_past()
    {
        using var client = NewClient();
        var venue = await CreateVenueAsync(client, -57, 0);
        await AddMatchAsync(venue, 1, cancelled: true);
        await AddMatchAsync(venue, -1);
        var upcoming = await AddMatchAsync(venue, 2);

        var page = await PageAsync(client, -57);
        Assert.Equal(upcoming.Id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("matchId").GetGuid());
    }

    [Fact]
    public async Task Six_decimal_and_two_decimal_coordinates_return_same_page()
    {
        using var client = NewClient();
        await AddMatchAsync(await CreateVenueAsync(client, -58.62, 0), 1);

        var rounded = await PageAsync(client, -58.62);
        var precise = await ReadPageAsync(client, "lat=-20.465123&lon=-58.624876");
        Assert.Equal(0, Assert.Single(rounded.GetProperty("items").EnumerateArray()).GetProperty("distanceKm").GetDouble());
        Assert.True(JsonElement.DeepEquals(rounded, precise));
    }

    [Fact]
    public async Task Response_has_no_venue_coordinates()
    {
        using var client = NewClient();
        await AddMatchAsync(await CreateVenueAsync(client, -59, 0), 1);

        var page = await PageAsync(client, -59);
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain("latitude", page.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", page.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Distance_rounded_to_one_decimal()
    {
        using var client = NewClient();
        await AddMatchAsync(await CreateVenueAsync(client, -60, 3.27), 1);

        var page = await PageAsync(client, -60);
        Assert.Equal(3.3, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("distanceKm").GetDouble());
    }

    [Fact]
    public async Task Pagination_by_cursor()
    {
        using var client = NewClient();
        var venue = await CreateVenueAsync(client, -61, 0);
        var later = await AddMatchAsync(venue, 3);
        var first = await AddMatchAsync(venue, 1);
        var tied = await AddMatchAsync(venue, 1);
        Guid[] expected = [.. new[] { first.Id, tied.Id }.Order(), later.Id];
        string? cursor = null;
        foreach (var id in expected)
        {
            var query = cursor is null ? "&limit=1" : $"&limit=1&cursor={Uri.EscapeDataString(cursor)}";
            var page = await PageAsync(client, -61, query);
            var item = Assert.Single(page.GetProperty("items").EnumerateArray());
            Assert.Equal(id, item.GetProperty("matchId").GetGuid());
            Assert.Equal(1, item.GetProperty("spotsLeft").GetInt32());
            cursor = page.GetProperty("nextCursor").GetString();
            Assert.Equal(id == later.Id, cursor is null);
        }
    }

    [Fact]
    public async Task No_venue_in_radius_returns_empty_page()
    {
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.AddScoped<IOpenMatchCatalog>(_ => new UnexpectedCatalog())));
        using var client = host.CreateClient();
        using var user = NewClient();
        client.DefaultRequestHeaders.Authorization = user.DefaultRequestHeaders.Authorization;

        var page = await PageAsync(client, -62);
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Null(page.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task Only_the_nearest_200_venues_are_considered()
    {
        using var client = NewClient();
        var near = await AddMatchAsync(await CreateVenueAsync(client, -63, 0), 1);
        await AddMatchAsync(await CreateVenueAsync(client, -63, 5), 1);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscoveryDbContext>();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO discovery.venues (id, name, latitude, longitude, created_by, created_at)
            SELECT gen_random_uuid(), 'Quadra', {Latitude} + n * 0.0001, -63, {Guid.NewGuid()}, now()
            FROM generate_series(1, 199) AS n
            """, Ct);

        var page = await PageAsync(client, -63);
        Assert.Equal(near.Id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("matchId").GetGuid());
    }

    [Theory]
    [InlineData("lon=0", "lat")]
    [InlineData("lat=0", "lon")]
    [InlineData("lat=90.0001&lon=0", "lat")]
    [InlineData("lat=-90.0001&lon=0", "lat")]
    [InlineData("lat=0&lon=180.0001", "lon")]
    [InlineData("lat=0&lon=-180.0001", "lon")]
    [InlineData("lat=NaN&lon=0", "lat")]
    [InlineData("lat=0&lon=NaN", "lon")]
    [InlineData("lat=0&lon=Infinity", "lon")]
    [InlineData("lat=0&lon=0&radiusKm=0", "radiusKm")]
    [InlineData("lat=0&lon=0&radiusKm=51", "radiusKm")]
    [InlineData("lat=0&lon=0&radiusKm=1.5", "radiusKm")]
    [InlineData("lat=0&lon=0&limit=51", "limit")]
    [InlineData("lat=0&lon=0&limit=0", "limit")]
    [InlineData("lat=-20.47&lon=-64&cursor=adulterado", "cursor")]
    public async Task Validation_400_for_missing_or_out_of_range_lat_lon_radius_and_bad_cursor(string query, string field)
    {
        using var client = NewClient();
        if (field == "cursor")
        {
            await CreateVenueAsync(client, -64, 0);
        }

        using var response = await client.GetAsync(new Uri($"{Path}?{query}", UriKind.Relative), Ct);
        using var problem = await ProblemAssertions.AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("Dados inválidos.", problem.RootElement.GetProperty("title").GetString());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Anonymous_returns_401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(Path, UriKind.Relative), Ct);
        using var problem = await ProblemAssertions.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Search_coordinates_are_absent_from_debug_logs()
    {
        using var logs = new CapturingLoggerProvider();
        await using var host = factory.WithWebHostBuilder(b => b
            .UseSetting("Logging:LogLevel:Default", "Debug")
            .UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Debug")
            .ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs)));
        using var user = NewClient();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = user.DefaultRequestHeaders.Authorization;
        logs.Clear();

        using var response = await client.GetAsync(new Uri($"{Path}?lat=-23.918765&lon=-65.138765", UriKind.Relative), Ct);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Debug);
        string[] coordinates = ["-23.918765", "-65.138765", "-23.92", "-65.14"];
        Assert.Empty(logs.Entries.Where(e => coordinates.Any(c => e.Message.Contains(c, StringComparison.Ordinal)))
            .Select(e => e.Category).Distinct());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_coordinates_are_absent_from_server_traces_even_with_unredacted_queries()
    {
        using var traces = new SearchTraceProcessor();
        await using var host = factory.WithWebHostBuilder(b => b
            .UseSetting("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "true")
            .ConfigureServices(services => services.AddOpenTelemetry().WithTracing(t => t.AddProcessor(traces))));
        using var client = host.CreateClient();
        using var user = NewClient();
        client.DefaultRequestHeaders.Authorization = user.DefaultRequestHeaders.Authorization;

        using var response = await client.GetAsync(new Uri($"{Path}?lat=-23.918765&lon=-65.138765", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var activity = await traces.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.True(activity.GetTagItem("url.query") is null, "O trace da busca incluiu a query.");
    }

    private static Task<JsonElement> PageAsync(HttpClient client, double longitude, string options = "") =>
        ReadPageAsync(client, FormattableString.Invariant($"lat={Latitude}&lon={longitude}{options}"));

    private static async Task<JsonElement> ReadPageAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri($"{Path}?{query}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<Guid> CreateVenueAsync(HttpClient client, double longitude, double distanceKm)
    {
        // Um grau de latitude mede aproximadamente 110,7 km neste paralelo.
        var body = new { name = "Campo público", addressText = "Praça central", latitude = Latitude + distanceKm / 110.7, longitude };
        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/venues", UriKind.Relative), body, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<Match> AddMatchAsync(Guid venue, int hours, bool cancelled = false, bool full = false)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(factory.Clock.GetUtcNow().ToUnixTimeSeconds());
        var organizer = Guid.NewGuid();
        var match = Match.Create(new NewMatch("Pelada aberta", now.AddHours(hours), "America/Sao_Paulo",
            60, 1, "public", null, null, venue), organizer, now.AddDays(-1)).Value;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        if (cancelled)
        {
            Assert.True(match.Cancel(organizer, now).IsSuccess);
        }

        if (full)
        {
            db.Registrations.Add(match.Register(Guid.NewGuid(), null, now).Value.Registration);
        }

        db.Matches.Add(match);
        await db.SaveChangesAsync(Ct);
        return match;
    }

    private sealed class SearchTraceProcessor : BaseProcessor<Activity>
    {
        public TaskCompletionSource<Activity> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void OnEnd(Activity data)
        {
            if (data.Kind == ActivityKind.Server && Equals(data.GetTagItem("url.path"), Path))
            {
                Completed.TrySetResult(data);
            }
        }
    }

    private sealed class UnexpectedCatalog : IOpenMatchCatalog
    {
        public Task<OpenMatchPage> ListUpcomingAtVenuesAsync(
            IReadOnlyCollection<Guid> venueIds, DateTimeOffset now, string? cursor, int limit, CancellationToken ct) =>
            throw new InvalidOperationException("Busca sem locais chamou o Matches.");
    }
}
