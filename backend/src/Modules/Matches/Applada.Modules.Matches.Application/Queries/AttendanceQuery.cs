using System.Text.Json;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Profiles.Contracts;

namespace Applada.Modules.Matches.Application.Queries;

public sealed record AttendanceEntry(Guid UserId, string? DisplayName, string? Status, DateTimeOffset? RecordedAt);

public sealed record AttendanceRow(Guid UserId, long QueueSeq, AttendanceStatus? Status, DateTimeOffset? RecordedAt);

public sealed class AttendanceQuery(IMatchQueries queries, IProfileNames profiles)
{
    public async Task<Result<Page<AttendanceEntry>>> HandleAsync(Guid matchId, Guid viewerId, string? cursor, int limit, CancellationToken ct)
    {
        if (!PageLimit.IsValid(limit))
        {
            return PageLimit.Invalid;
        }

        long? afterQueueSeq = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!Cursor.TryDecode(cursor, Cursor.AttendanceKind, out var values)
                || values.Length != 1 || values[0].ValueKind != JsonValueKind.Number
                || !values[0].TryGetInt64(out var queueSeq) || queueSeq < 1)
            {
                return MatchErrors.InvalidCursor;
            }

            afterQueueSeq = queueSeq;
        }

        var result = await queries.ListAttendanceAsync(matchId, viewerId, afterQueueSeq, limit + 1, ct);
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
        var items = page.Select(r => new AttendanceEntry(r.UserId, names.GetValueOrDefault(r.UserId), r.Status?.ToContractText(), r.RecordedAt)).ToArray();
        var next = rows.Count > limit ? Cursor.Encode(Cursor.AttendanceKind, page[^1].QueueSeq) : null;
        return new Page<AttendanceEntry>(items, next);
    }
}
