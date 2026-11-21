using System.Text.Json;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Communities.Contracts;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.Queries;

/// <summary>
/// Próximas partidas de um grupo (<c>starts_at &gt;= now</c>, só as agendadas), por <c>(starts_at, id)</c>, visíveis a
/// qualquer membro do grupo. Quem não é membro e um grupo inexistente recebem o mesmo <c>group-not-found</c>, para não
/// revelar que o grupo existe. Partidas canceladas ou passadas não aparecem.
/// </summary>
public sealed class GroupMatchesQuery(IMatchQueries queries, IGroupMembership groups)
{
    public async Task<Result<Page<MyMatchSummary>>> HandleAsync(
        Guid groupId,
        Guid userId,
        string? cursor,
        int limit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!PageLimit.IsValid(limit))
        {
            return PageLimit.Invalid;
        }

        MyMatchesKey? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TryReadKey(cursor, out var key))
            {
                return MatchErrors.InvalidCursor;
            }

            after = key;
        }

        if (await groups.GetRoleAsync(groupId, userId, ct) is null)
        {
            return MatchErrors.GroupNotFound;
        }

        var rows = await queries.ListGroupUpcomingAsync(groupId, userId, now.ToUniversalTime(), after, limit + 1, ct);

        var items = rows.Take(limit)
            .Select(r => new MyMatchSummary(
                r.Id,
                r.Title,
                r.StartsAt,
                StatusText.Of(r.Status),
                r.ConfirmedCount,
                r.Capacity,
                r.MyStatus is { } mine ? StatusText.Of(mine) : null))
            .ToArray();
        string? next = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1];
            next = Cursor.Encode(Cursor.GroupMatchesKind, last.StartsAt, last.Id);
        }

        return new Page<MyMatchSummary>(items, next);
    }

    /// <summary>Exatamente <c>[startsAt, id]</c>: data ISO 8601 (levada a UTC, como o banco compara) e uuid.</summary>
    private static bool TryReadKey(string cursor, out MyMatchesKey key)
    {
        key = null!;
        if (!Cursor.TryDecode(cursor, Cursor.GroupMatchesKind, out var values)
            || values.Length != 2
            || values[0].ValueKind != JsonValueKind.String
            || values[1].ValueKind != JsonValueKind.String
            || !values[0].TryGetDateTimeOffset(out var startsAt)
            || !values[1].TryGetGuid(out var id))
        {
            return false;
        }

        key = new MyMatchesKey(startsAt.ToUniversalTime(), id);
        return true;
    }
}
