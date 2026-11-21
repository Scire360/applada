using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Profiles.Contracts;

namespace Applada.Modules.Matches.Application.Queries;

public sealed record AttendanceEntry(Guid UserId, string? DisplayName, string? Status, DateTimeOffset? RecordedAt);

public sealed record AttendanceRow(Guid UserId, long QueueSeq, string? Status, DateTimeOffset? RecordedAt);

public sealed class AttendanceQuery(IMatchQueries queries, IProfileNames profiles)
{
    public async Task<Result<Page<AttendanceEntry>>> HandleAsync(Guid matchId, Guid viewerId, string? cursor, int limit, CancellationToken ct)
    {
        if (!PageLimit.IsValid(limit))
        {
            return PageLimit.Invalid;
        }

        ParticipantsKey? after = null;
        if (!string.IsNullOrEmpty(cursor) && !ParticipantsKey.TryRead(cursor, out after))
        {
            return MatchErrors.InvalidCursor;
        }

        var result = await queries.ListAttendanceAsync(matchId, viewerId, after, limit + 1, ct);
        if (!result.IsSuccess)
        {
            return result.Error;
        }

        var rows = result.Value;
        if (rows.Count == 0)
        {
            return new Page<AttendanceEntry>([], null);
        }

        var page = rows.Take(limit).ToArray();
        var names = await profiles.ListAsync(page.Select(r => r.UserId).ToArray(), ct);
        var items = page.Select(r => new AttendanceEntry(r.UserId, names.GetValueOrDefault(r.UserId), r.Status, r.RecordedAt)).ToArray();
        var next = rows.Count > limit ? Cursor.Encode(Cursor.ParticipantsKind, ParticipantsKey.ConfirmedRank, page[^1].QueueSeq) : null;
        return new Page<AttendanceEntry>(items, next);
    }
}
