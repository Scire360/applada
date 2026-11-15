using System.Diagnostics;
using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Persistence.Idempotency;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Matches.Infrastructure;

/// <summary>
/// Sessão de uma chamada de <see cref="MatchesUnitOfWork.ExecuteAsync{T}"/>: tudo o que ela lê fica rastreado e o
/// que ela adiciona só vai ao banco no <c>SaveChanges</c> da unidade de trabalho, dentro da mesma transação.
/// </summary>
internal sealed class MatchesSession(MatchesDbContext db, TimeProvider timeProvider) : IMatchesSession
{
    public Task<Match?> GetMatchForUpdateAsync(Guid matchId, CancellationToken ct) =>
        db.Matches
            .FromSql($"SELECT * FROM matches.matches WHERE id = {matchId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

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
        db.OutboxMessages.Add(OutboxMessage.From(evt, CorrelationId(), timeProvider.GetUtcNow()));
    }

    /// <summary>Trace da requisição corrente (sem depender de HTTP); fora de um trace, um id novo.</summary>
    private static string CorrelationId() =>
        Activity.Current is { } activity && activity.TraceId != default
            ? activity.TraceId.ToString()
            : Guid.NewGuid().ToString("N");
}
