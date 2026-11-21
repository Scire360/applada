using System.Text.Json;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.Queries;

/// <summary>
/// Lista as partidas do usuário por <c>(starts_at, id)</c>. Canceladas aparecem só enquanto <c>starts_at &gt;= now</c>.
/// Quem organiza também vê as partidas agendadas já iniciadas com a chamada aberta. O cursor só posiciona a leitura.
/// </summary>
public sealed class MyMatchesQuery(IMatchQueries queries)
{
    public const string OrganizerRole = "organizer";
    public const string ParticipantRole = "participant";

    private static readonly Error InvalidRole = MatchErrors.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["role"] = [$"Informe role={OrganizerRole} ou role={ParticipantRole}."],
    });

    public async Task<Result<Page<MyMatchSummary>>> HandleAsync(
        Guid userId,
        string role,
        string? cursor,
        int limit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var organizer = string.Equals(role, OrganizerRole, StringComparison.Ordinal);
        if (!organizer && !string.Equals(role, ParticipantRole, StringComparison.Ordinal))
        {
            return InvalidRole;
        }

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

        var nowUtc = now.ToUniversalTime();
        var rows = organizer
            ? await queries.ListOrganizedAsync(userId, nowUtc, after, limit + 1, ct)
            : await queries.ListParticipatingAsync(userId, nowUtc, after, limit + 1, ct);

        var items = rows.Take(limit)
            .Select(r => new MyMatchSummary(
                r.Id,
                r.Title,
                r.StartsAt,
                r.DurationMinutes,
                StatusText.Of(r.Status),
                r.ConfirmedCount,
                r.Capacity,
                r.MyStatus is { } mine ? StatusText.Of(mine) : null))
            .ToArray();
        string? next = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1];
            next = Cursor.Encode(Cursor.MyMatchesKind, last.StartsAt, last.Id);
        }

        return new Page<MyMatchSummary>(items, next);
    }

    /// <summary>
    /// Exatamente <c>[startsAt, id]</c>: data ISO 8601 e uuid. A data é levada a UTC (o banco compara instantes e o
    /// Npgsql só grava <c>timestamptz</c> com offset zero).
    /// </summary>
    private static bool TryReadKey(string cursor, out MyMatchesKey key)
    {
        key = null!;
        if (!Cursor.TryDecode(cursor, Cursor.MyMatchesKind, out var values)
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
