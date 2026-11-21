using System.Text.Json;
using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Contracts;
using Applada.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Matches.Infrastructure;

internal sealed class OpenMatchCatalog(MatchesDbContext db) : IOpenMatchCatalog
{
    public bool IsValidCursor(string cursor) => TryDecodeCursor(cursor, out _, out _);

    public async Task<OpenMatchPage> ListUpcomingAtVenuesAsync(
        IReadOnlyCollection<Guid> venueIds, DateTimeOffset now, string? cursor, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(venueIds);
        if (!PageLimit.IsValid(limit))
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, $"O limite deve ser um inteiro entre 1 e {PageLimit.Max}.");
        }

        var nowUtc = now.ToUniversalTime();
        var ids = venueIds.ToArray();
        var matches = db.Matches.AsNoTracking().Where(m => m.Status == MatchStatus.Scheduled
            && m.Visibility == MatchVisibility.Public && m.StartsAt > nowUtc
            && m.VenueId.HasValue && ids.Contains(m.VenueId.Value));
        if (cursor is not null)
        {
            if (!TryDecodeCursor(cursor, out var start, out var id))
            {
                throw new ArgumentException("O cursor de paginação é inválido.", nameof(cursor));
            }

            var startUtc = start.ToUniversalTime();
            matches = matches.Where(m => EF.Functions.GreaterThan(ValueTuple.Create(m.StartsAt, m.Id), ValueTuple.Create(startUtc, id)));
        }

        if (ids.Length == 0)
        {
            return new OpenMatchPage([], null);
        }

        var items = await matches.OrderBy(m => m.StartsAt).ThenBy(m => m.Id)
            .Take(limit + 1)
            .Select(m => new OpenMatchSummary(m.Id, m.VenueId!.Value, m.Title, m.StartsAt, m.TimeZone, m.Capacity, m.ConfirmedCount))
            .ToListAsync(ct);
        if (items.Count <= limit)
        {
            return new OpenMatchPage(items, null);
        }

        items.RemoveAt(limit);
        var last = items[^1];
        return new OpenMatchPage(items, Cursor.Encode(Cursor.OpenMatchesKind, last.StartsAt, last.MatchId));
    }

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset start, out Guid id)
    {
        start = default;
        id = default;
        return Cursor.TryDecode(cursor, Cursor.OpenMatchesKind, out var keys) && keys.Length == 2
            && keys[0].ValueKind == JsonValueKind.String && keys[0].TryGetDateTimeOffset(out start)
            && keys[1].ValueKind == JsonValueKind.String && keys[1].TryGetGuid(out id);
    }
}
