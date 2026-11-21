using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Contracts;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.RecordAttendance;

public sealed record RecordedAttendance(Guid UserId, AttendanceStatus Status, DateTimeOffset RecordedAt);

public sealed class RecordAttendanceHandler(IMatchesUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<Result<RecordedAttendance>> HandleAsync(Guid matchId, Guid actorId, Guid userId, string status, CancellationToken ct)
    {
        if (AttendanceStatusText.Parse(status) is not { } attendance)
        {
            return MatchErrors.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["status"] = ["Informe present ou absent para a presença."],
            });
        }

        return await unitOfWork.ExecuteAsync<RecordedAttendance>(async (session, token) =>
        {
            var now = clock.GetUtcNow();
            var allowed = await CheckEligibilityAsync(session, matchId, userId, actorId, now, token);
            if (!allowed.IsSuccess)
            {
                return allowed.Error;
            }

            var (recordedAt, changed) = await session.UpsertAttendanceAsync(matchId, userId, attendance, actorId, now, token);
            if (changed)
            {
                // No primeiro INSERT, a FK espera por saídas/cancelamentos iniciados antes da abertura.
                // No PostgreSQL 17, RETURNING não identifica o ramo do upsert e xmax é metadado de MVCC.
                // Revalidamos toda mudança para que um INSERT nunca publique presença inelegível.
                allowed = await CheckEligibilityAsync(session, matchId, userId, actorId, now, token);
                if (!allowed.IsSuccess)
                {
                    return allowed.Error;
                }

                session.Publish(new AttendanceRecordedV1(matchId, userId, attendance.ToContractText(), recordedAt));
            }

            return new RecordedAttendance(userId, attendance, recordedAt);
        }, ct);
    }

    private static async Task<Result> CheckEligibilityAsync(IMatchesSession session, Guid matchId, Guid userId, Guid actorId,
        DateTimeOffset now, CancellationToken ct)
    {
        var subject = await session.GetAttendanceSubjectAsync(matchId, userId, ct);
        return subject is null
            ? MatchErrors.MatchNotFound
            : subject.Match.CanRecordAttendance(actorId, subject.Registration, now);
    }
}
