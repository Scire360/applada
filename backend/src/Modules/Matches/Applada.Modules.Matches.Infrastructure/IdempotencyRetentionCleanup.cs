using Applada.BuildingBlocks.Persistence.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace Applada.Modules.Matches.Infrastructure;

/// <summary>
/// Apaga de <c>matches.idempotency_keys</c> as respostas guardadas há mais de <see cref="IdempotencyRecord.Retention"/>
/// (spec §8; LGPD: retenção mínima). Em lotes pelo índice de <c>created_at</c>, cada um na sua transação curta, para não
/// segurar locks numa tabela grande. Chamada pelo <see cref="IdempotencyCleanupService"/>.
/// </summary>
public sealed class IdempotencyRetentionCleanup(MatchesDbContext db, TimeProvider timeProvider)
{
    public const int DefaultBatchSize = 1000;

    /// <summary>Uma passada completa: repete o DELETE em lotes até não sobrar entrada vencida. Devolve o total apagado.</summary>
    public Task<int> DeleteExpiredAsync(CancellationToken ct) => DeleteExpiredAsync(DefaultBatchSize, ct);

    /// <inheritdoc cref="DeleteExpiredAsync(CancellationToken)"/>
    public async Task<int> DeleteExpiredAsync(int batchSize, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        // Corte fixo na passada: linhas que vencerem durante ela ficam para a próxima.
        var cutoff = timeProvider.GetUtcNow() - IdempotencyRecord.Retention;
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.IdempotencyRecords
                .Where(r => r.CreatedAt < cutoff)
                .OrderBy(r => r.CreatedAt)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);
            total += deleted;
        }
        while (deleted == batchSize);

        return total;
    }
}
