using System.Data;
using Applada.Api.IntegrationTests.Infrastructure;
using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Contracts;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Applada.Api.IntegrationTests;

[Collection("api")]
public class OpenMatchCatalogTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_only_future_scheduled_matches_at_requested_venues_in_keyset_order()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(factory.Clock.GetUtcNow().ToUnixTimeSeconds());
        var venueA = Guid.NewGuid();
        var venueB = Guid.NewGuid();
        var organizer = Guid.NewGuid();
        Match NewMatchAt(Guid? venue, DateTimeOffset start) => Match.Create(
            new NewMatch("Pelada aberta", start, "America/Sao_Paulo", 60, 1, "public", null, null, venue),
            organizer, now.AddDays(-2)).Value;

        var full = NewMatchAt(venueB, now.AddHours(1));
        var registration = full.Register(Guid.NewGuid(), null, now).Value.Registration;
        var tied = NewMatchAt(venueA, full.StartsAt);
        var later = NewMatchAt(venueB, now.AddHours(2));
        var cancelled = NewMatchAt(venueA, later.StartsAt);
        Assert.True(cancelled.Cancel(organizer, now).IsSuccess);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        db.Matches.AddRange(full, tied, later, cancelled, NewMatchAt(null, later.StartsAt),
            NewMatchAt(Guid.NewGuid(), later.StartsAt), NewMatchAt(venueA, now), NewMatchAt(venueB, now.AddHours(-1)));
        db.Registrations.Add(registration);
        await db.SaveChangesAsync(Ct);

        var catalog = scope.ServiceProvider.GetRequiredService<IOpenMatchCatalog>();
        Guid[] venues = [venueA, venueB];
        Match[] expected = [.. new[] { full, tied }.OrderBy(m => m.Id), later];
        var all = await catalog.ListUpcomingAtVenuesAsync(venues, now, null, 3, Ct);
        Assert.Equal(expected.Select(m => new OpenMatchSummary(m.Id, m.VenueId!.Value, m.Title, m.StartsAt,
            m.TimeZone, m.Capacity, m.ConfirmedCount)), all.Items);
        Assert.Null(all.NextCursor);

        string? cursor = null;
        foreach (var match in expected)
        {
            var page = await catalog.ListUpcomingAtVenuesAsync(venues, now, cursor, 1, Ct);
            Assert.Equal(match.Id, Assert.Single(page.Items).MatchId);
            cursor = page.NextCursor;
            Assert.Equal(match == later, cursor is null);
            if (cursor is not null)
            {
                Assert.True(catalog.IsValidCursor(cursor));
            }
        }
    }

    [Fact]
    public async Task Empty_venues_return_empty_page_without_opening_a_connection()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IOpenMatchCatalog>();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var connection = db.Database.GetDbConnection();
        Assert.Equal(ConnectionState.Closed, connection.State);
        var opened = false;
        connection.StateChange += (_, e) => opened |= e.CurrentState == ConnectionState.Open;

        var page = await catalog.ListUpcomingAtVenuesAsync([], factory.Clock.GetUtcNow(), null, 20, Ct);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.False(opened);
    }

    [Fact]
    public async Task Rejects_malformed_cursor_and_cursor_from_another_listing()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IOpenMatchCatalog>();
        var now = factory.Clock.GetUtcNow();
        string[] invalid =
        [
            "???",
            Cursor.Encode(Cursor.GroupMatchesKind, now, Guid.NewGuid()),
            Cursor.Encode(Cursor.OpenMatchesKind, now),
            Cursor.Encode(Cursor.OpenMatchesKind, 1, Guid.NewGuid()),
            Cursor.Encode(Cursor.OpenMatchesKind, now, 1),
        ];
        foreach (var cursor in invalid)
        {
            Assert.False(catalog.IsValidCursor(cursor));
            await Assert.ThrowsAsync<ArgumentException>("cursor",
                () => catalog.ListUpcomingAtVenuesAsync([Guid.NewGuid()], now, cursor, 10, Ct));
        }
    }
}
