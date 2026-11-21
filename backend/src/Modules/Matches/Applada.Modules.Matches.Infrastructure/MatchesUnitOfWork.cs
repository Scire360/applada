using System.Data;
using Applada.BuildingBlocks.Domain;
using Applada.BuildingBlocks.Persistence.Idempotency;
using Applada.Modules.Matches.Application;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Applada.Modules.Matches.Infrastructure;

/// <summary>
/// Transação <c>READ COMMITTED</c> com <c>lock_timeout</c> local por chamada (spec §9). O lock da partida vem de
/// <see cref="IMatchesSession.GetMatchForUpdateAsync"/> e vale até o commit ou rollback. Tudo o que <c>work</c>
/// adiciona — partida, inscrições, idempotência e outbox — vai num único <c>SaveChanges</c> antes do commit:
/// ou tudo persiste, ou nada. Sem <c>EnableRetryOnFailure</c> (incompatível com a transação explícita).
/// </summary>
public sealed partial class MatchesUnitOfWork(
    MatchesDbContext db,
    TimeProvider timeProvider,
    IOptions<MatchesPersistenceOptions> options,
    MatchesMetrics metrics,
    ILogger<MatchesUnitOfWork> logger) : IMatchesUnitOfWork
{
    public Task<IdempotencyEntry?> FindActiveIdempotencyAsync(Guid userId, string key, CancellationToken ct)
    {
        var since = timeProvider.GetUtcNow() - IdempotencyRecord.Retention;
        return db.IdempotencyRecords.AsNoTracking()
            .Where(r => r.UserId == userId && r.Key == key && r.CreatedAt >= since)
            .Select(r => new IdempotencyEntry(r.UserId, r.Key, r.RequestHash, r.StatusCode, r.ResponseBody))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<Result<T>> ExecuteAsync<T>(Func<IMatchesSession, CancellationToken, Task<Result<T>>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("Unidade de trabalho de Matches aninhada em outra transação.");
        }

        if (db.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("Há alterações pendentes no contexto feitas fora da unidade de trabalho.");
        }

        // Entidade rastreada antes (leitura sem lock) seria devolvida no lugar dos valores lidos sob o lock.
        db.ChangeTracker.Clear();

        var lockTimeout = options.Value.LockTimeout;
        var session = new MatchesSession(db, timeProvider);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // SET não aceita parâmetro; set_config(..., true) é o SET LOCAL parametrizado (vale só nesta transação).
            var setting = $"{(long)lockTimeout.TotalMilliseconds}ms";
            await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {setting}, true)", ct);

            var result = await work(session, ct);
            if (!result.IsSuccess)
            {
                await RollbackAsync(transaction);
                return result;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (FindPostgresException(ex) is { SqlState: PostgresErrorCodes.LockNotAvailable })
        {
            await RollbackAsync(transaction);
            metrics.LockTimedOut();
            LogLockTimeout(logger, session.LockedMatchId, lockTimeout);
            return MatchErrors.LockTimeout;
        }
        catch (Exception ex) when (IsIdempotencyKeyConflict(ex))
        {
            await RollbackAsync(transaction);
            throw new IdempotencyConflictException("A Idempotency-Key foi gravada por outra requisição.", ex);
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
    }

    private static bool IsIdempotencyKeyConflict(Exception ex) =>
        FindPostgresException(ex) is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IdempotencyModelBuilderExtensions.PrimaryKeyName,
        };

    /// <summary>O EF entrega o erro do banco direto (consultas) ou embrulhado (<c>DbUpdateException</c> no SaveChanges).</summary>
    private static PostgresException? FindPostgresException(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    /// <summary>
    /// Desfaz a transação e descarta o que ficou rastreado, para que nada da tentativa falha seja gravado depois por
    /// quem reutilizar o contexto. Falha no rollback não esconde o erro original: a transação morre com a conexão.
    /// </summary>
    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        db.ChangeTracker.Clear();
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            LogRollbackFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lock da partida {MatchId} não obtido em {LockTimeout}; operação devolvida como lock-timeout.")]
    private static partial void LogLockTimeout(ILogger logger, Guid? matchId, TimeSpan lockTimeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rollback da unidade de trabalho de Matches falhou.")]
    private static partial void LogRollbackFailed(ILogger logger, Exception exception);
}
