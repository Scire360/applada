using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Persistence.Idempotency;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Matches.Infrastructure;

/// <summary>
/// Sessão de uma chamada de <see cref="MatchesUnitOfWork.ExecuteAsync{T}"/>. O upsert de presença e o
/// <c>SaveChanges</c> que persiste o outbox participam da mesma transação.
/// </summary>
internal sealed class MatchesSession(MatchesDbContext db, TimeProvider timeProvider) : IMatchesSession
{
    /// <summary>Última partida cujo lock esta sessão pediu (para o log de lock-timeout).</summary>
    public Guid? LockedMatchId { get; private set; }

    /// <inheritdoc/>
    public Task<Match?> GetMatchForUpdateAsync(Guid matchId, CancellationToken ct)
    {
        LockedMatchId = matchId;
        return db.Matches
            .FromSql($"SELECT * FROM matches.matches WHERE id = {matchId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
    }

    public Task<AttendanceSubject?> GetAttendanceSubjectAsync(Guid matchId, Guid userId, CancellationToken ct) =>
        db.Matches.AsNoTracking().Where(m => m.Id == matchId)
            .Select(m => new AttendanceSubject(m, db.Registrations.FirstOrDefault(r => r.MatchId == m.Id && r.UserId == userId)))
            .SingleOrDefaultAsync(ct);

    public async Task<(DateTimeOffset RecordedAt, bool Changed)> UpsertAttendanceAsync(
        Guid matchId, Guid userId, string status, Guid recordedBy, DateTimeOffset recordedAt, CancellationToken ct)
    {
        var changed = await db.Database.SqlQuery<DateTimeOffset>($"""
            INSERT INTO matches.attendance (match_id, user_id, status, recorded_by, recorded_at)
            VALUES ({matchId}, {userId}, {status}, {recordedBy}, {recordedAt})
            ON CONFLICT (match_id, user_id) DO UPDATE
            SET status = excluded.status, recorded_by = excluded.recorded_by, recorded_at = excluded.recorded_at
            WHERE matches.attendance.status <> excluded.status
            RETURNING recorded_at AS "Value"
            """).ToListAsync(ct);
        if (changed.Count != 0)
        {
            return (changed.Single(), true);
        }

        // ON CONFLICT mantém a linha travada até o commit mesmo quando o WHERE impede o UPDATE.
        var original = await db.Attendance.Where(a => a.MatchId == matchId && a.UserId == userId)
            .Select(a => a.RecordedAt).SingleAsync(ct);
        return (original, false);
    }

    public Task<Registration?> FindRegistrationAsync(Guid matchId, Guid userId, CancellationToken ct) =>
        db.Registrations.SingleOrDefaultAsync(r => r.MatchId == matchId && r.UserId == userId, ct);

    public async Task<IReadOnlyList<Registration>> GetWaitlistHeadAsync(Guid matchId, int count, CancellationToken ct)
    {
        // LIMIT negativo é erro no PostgreSQL; zero não precisa ir ao banco.
        if (count <= 0)
        {
            return [];
        }

        return await db.Registrations
            .Where(r => r.MatchId == matchId && r.Status == RegistrationStatus.Waitlisted)
            .OrderBy(r => r.QueueSeq)
            .Take(count)
            .ToListAsync(ct);
    }

    public Task<int> CountWaitlistedUpToAsync(Guid matchId, long queueSeq, CancellationToken ct) =>
        db.Registrations.CountAsync(
            r => r.MatchId == matchId && r.Status == RegistrationStatus.Waitlisted && r.QueueSeq <= queueSeq,
            ct);

    public async Task<IdempotencyEntry?> FindIdempotencyAsync(Guid userId, string key, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.UserId == userId && r.Key == key, ct);
        if (record is null)
        {
            return null;
        }

        var expiredBefore = timeProvider.GetUtcNow() - IdempotencyRecord.Retention;
        if (record.CreatedAt >= expiredBefore)
        {
            return new IdempotencyEntry(record.UserId, record.Key, record.RequestHash, record.StatusCode, record.ResponseBody);
        }

        // Vencida: conta como ausente e sai da tabela nesta transação, para a chave poder ser gravada de novo (PK
        // (user_id, key)). Rollback devolve a linha. Concorrência: quem chegar depois espera este DELETE, não acha a
        // linha nova (fora do snapshot do comando), e o INSERT dele bate na PK → IdempotencyConflictException → releitura.
        await db.IdempotencyRecords
            .Where(r => r.UserId == userId && r.Key == key && r.CreatedAt < expiredBefore)
            .ExecuteDeleteAsync(ct);
        return null;
    }

    public void AddMatch(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        db.Matches.Add(match);
    }

    public void AddRegistration(Registration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        db.Registrations.Add(registration);
    }

    public void AddIdempotency(IdempotencyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            UserId = entry.UserId,
            Key = entry.Key,
            RequestHash = entry.RequestHash,
            StatusCode = entry.StatusCode,
            ResponseBody = entry.ResponseBody,
            CreatedAt = timeProvider.GetUtcNow(),
        });
    }

    public void Publish(IntegrationEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        // Trace da requisição corrente (sem depender de HTTP); fora de um trace, um id novo.
        db.OutboxMessages.Add(OutboxMessage.From(evt, CorrelationId.Current(), timeProvider.GetUtcNow()));
    }
}
