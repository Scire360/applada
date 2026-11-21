using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Discovery.Contracts;
using Applada.Modules.Discovery.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class VenuesTests(ApiFactory factory)
{
    private static readonly Uri Venues = new("/api/v1/venues", UriKind.Relative);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private HttpClient NewClient() => factory.CreateClientFor($"venues-{Guid.NewGuid():N}");
    private static JsonObject ValidBody() => new()
    {
        ["name"] = "  Quadra central  ",
        ["addressText"] = "Praça central",
        ["latitude"] = -20.469716,
        ["longitude"] = -54.620126,
    };

    [Fact]
    public async Task Create_then_get_venue()
    {
        using var owner = NewClient();
        using var created = await owner.PostAsJsonAsync(Venues, ValidBody(), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal($"/api/v1/venues/{id}", created.Headers.Location?.OriginalString);
        Assert.Equal("Quadra central", body.GetProperty("name").GetString());
        Assert.Equal("Praça central", body.GetProperty("addressText").GetString());
        Assert.Equal(-20.46972, body.GetProperty("latitude").GetDouble());
        Assert.Equal(-54.62013, body.GetProperty("longitude").GetDouble());

        using var reader = NewClient();
        var read = await reader.GetFromJsonAsync<JsonElement>(created.Headers.Location, Ct);
        Assert.True(JsonElement.DeepEquals(body, read));
    }

    [Theory]
    [InlineData(false, 8192, HttpStatusCode.Created)]
    [InlineData(true, 8192, HttpStatusCode.Created)]
    [InlineData(false, 8193, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(true, 8193, HttpStatusCode.RequestEntityTooLarge)]
    public async Task Create_limits_body_to_8kb(bool chunked, int size, HttpStatusCode expected)
    {
        var json = """{"name":"Arena","latitude":0,"longitude":0}""".PadRight(size);
        var bytes = Encoding.UTF8.GetBytes(json);
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Venues)
        {
            Content = chunked ? new StreamContent(new UnknownLengthStream(bytes)) : new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.TransferEncodingChunked = chunked;
        Assert.Equal(chunked ? (long?)null : size, request.Content.Headers.ContentLength);

        using var response = await client.SendAsync(request, Ct);
        if (expected == HttpStatusCode.RequestEntityTooLarge)
        {
            using var problem = await MatchesApi.AssertProblemAsync(response, expected, "payload-too-large");
        }
        else
        {
            Assert.Equal(expected, response.StatusCode);
        }
    }

    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    [Theory]
    [InlineData("name", "ab")]
    [InlineData("name", null)]
    [InlineData("name", "Arena\tcentral")]
    [InlineData("addressText", "Rua\ncentral")]
    [InlineData("name", "A🏟")]
    [InlineData("addressText", "too-long")]
    [InlineData("latitude", "missing")]
    [InlineData("longitude", "missing")]
    [InlineData("latitude", "outside")]
    [InlineData("longitude", "outside")]
    [InlineData("latitude", "NaN")]
    [InlineData("longitude", "NaN")]
    public async Task Create_returns_400_for_each_invalid_field(string field, string? invalid)
    {
        var body = ValidBody();
        if (invalid == "missing")
        {
            body.Remove(field);
        }
        else
        {
            body[field] = invalid switch
            {
                "outside" => JsonValue.Create(field == "latitude" ? 90.0001 : -180.0001),
                "too-long" => JsonValue.Create(new string('a', 201)),
                _ => JsonValue.Create(invalid),
            };
        }

        using var client = NewClient();
        using var response = await client.PostAsJsonAsync(Venues, body, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Anonymous_returns_401()
    {
        using var client = factory.CreateClient();
        using var create = await client.PostAsJsonAsync(Venues, ValidBody(), Ct);
        using var get = await client.GetAsync(new Uri($"/api/v1/venues/{Guid.NewGuid()}", UriKind.Relative), Ct);
        using var list = await client.GetAsync(new Uri("/api/v1/me/venues", UriKind.Relative), Ct);
        Assert.All(new[] { create, get, list }, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    [Fact]
    public async Task Unknown_venue_returns_404()
    {
        using var client = NewClient();
        using var response = await client.GetAsync(new Uri($"/api/v1/venues/{Guid.NewGuid()}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("urn:applada:problem:venue-not-found", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task My_venues_is_isolated_per_user_and_paginates()
    {
        using var owner = NewClient();
        using var other = NewClient();
        var first = await CreateAsync(owner);
        var second = await CreateAsync(owner); // Mesmo created_at no relógio da fixture.
        await CreateAsync(other);
        var initialTime = factory.Clock.GetUtcNow();
        Guid newest;
        try
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
            newest = await CreateAsync(owner);
        }
        finally
        {
            factory.Clock.SetUtcNow(initialTime);
        }

        Guid[] expected = [newest, .. new[] { first, second }.OrderDescending()];
        string? cursor = null;
        foreach (var id in expected)
        {
            var path = new Uri($"/api/v1/me/venues?limit=1&cursor={cursor}", UriKind.Relative);
            var page = await owner.GetFromJsonAsync<JsonElement>(path, Ct);
            Assert.Equal(id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            cursor = page.GetProperty("nextCursor").GetString();
            Assert.Equal(id == expected[^1], cursor is null);
        }
    }

    [Theory]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=51", "limit")]
    [InlineData("limit=x", "limit")]
    [InlineData("cursor=invalid", "cursor")]
    public async Task Invalid_pagination_returns_400(string query, string field)
    {
        using var client = NewClient();
        using var response = await client.GetAsync(new Uri($"/api/v1/me/venues?{query}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Generated_location_column_matches_coordinates()
    {
        using var client = NewClient();
        var id = await CreateAsync(client);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscoveryDbContext>();
        var coordinates = await db.Database.SqlQuery<Coordinates>($"""
            SELECT ST_Y(location::geometry) AS "Latitude", ST_X(location::geometry) AS "Longitude",
                   ST_SRID(location::geometry) AS "Srid"
            FROM discovery.venues WHERE id = {id}
            """).SingleAsync(Ct);
        Assert.Equal(new Coordinates(-20.46972, -54.62013, 4326), coordinates);
    }

    [Fact]
    public async Task VenueDirectory_exists_true_and_false()
    {
        using var client = NewClient();
        var id = await CreateAsync(client);
        await using var scope = factory.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IVenueDirectory>();
        Assert.True(await directory.ExistsAsync(id, Ct));
        Assert.False(await directory.ExistsAsync(Guid.NewGuid(), Ct));
    }

    private sealed record Coordinates(double Latitude, double Longitude, int Srid);

    private static async Task<Guid> CreateAsync(HttpClient client)
    {
        var body = ValidBody();
        body.Remove("addressText");
        using var response = await client.PostAsJsonAsync(Venues, body, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var venue = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(JsonValueKind.Null, venue.GetProperty("addressText").ValueKind);
        return venue.GetProperty("id").GetGuid();
    }
}
