using Applada.Api.IntegrationTests.Infrastructure;
using Applada.BuildingBlocks.Persistence.Idempotency;
using Applada.Modules.Matches.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Applada.Api.IntegrationTests;

/// <summary>
/// Retenção de <c>matches.idempotency_keys</c> (spec §8, LGPD): respostas guardadas há mais de 24 h saem da tabela
/// por uma limpeza periódica, não só quando a mesma chave é reutilizada.
/// </summary>
[Collection("api")]
public class IdempotencyCleanupTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cleanup_pass_deletes_entries_older_than_24h_in_batches_and_keeps_fresh_ones()
    {
        var now = factory.Clock.GetUtcNow();
        var expired = Enumerable.Range(0, 5)
            .Select(i => NewRecord(now - IdempotencyRecord.Retention - TimeSpan.FromSeconds(1) - TimeSpan.FromMinutes(i)))
            .ToList();
        var fresh = NewRecord(now - IdempotencyRecord.Retention + TimeSpan.FromSeconds(1));
        var recent = NewRecord(now);
        await InsertAsync([.. expired, fresh, recent]);
        var userIds = expired.Append(fresh).Append(recent).Select(r => r.UserId).ToList();
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var cleanup = scope.ServiceProvider.GetRequiredService<IdempotencyRetentionCleanup>();

                // Lote de 2 < 5 vencidas: uma passada repete o DELETE até esvaziar.
                var deleted = await cleanup.DeleteExpiredAsync(batchSize: 2, Ct);

                Assert.True(deleted >= expired.Count, $"Apagou {deleted}.");
            }

            var remaining = await RemainingUserIdsAsync(userIds);
            Assert.Equal(new HashSet<Guid> { fresh.UserId, recent.UserId }, remaining.ToHashSet());
        }
        finally
        {
            await DeleteAsync(userIds);
        }
    }

    [Fact]
    public void Cleanup_runs_as_a_hosted_service_of_the_matches_module()
    {
        // Ligado por padrão; os testes desligam por configuração e chamam a limpeza diretamente.
        Assert.Contains(
            factory.Services.GetServices<IHostedService>(),
            s => s.GetType().Name == "IdempotencyCleanupService");
    }

    private static IdempotencyRecord NewRecord(DateTimeOffset createdAt) => new()
    {
        UserId = Guid.CreateVersion7(),
        Key = $"key-{Guid.NewGuid():N}",
        RequestHash = "hash",
        StatusCode = 201,
        ResponseBody = "{}",
        CreatedAt = createdAt,
    };

    private async Task InsertAsync(IEnumerable<IdempotencyRecord> records)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        db.IdempotencyRecords.AddRange(records);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<List<Guid>> RemainingUserIdsAsync(List<Guid> userIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        return await db.IdempotencyRecords.Where(r => userIds.Contains(r.UserId)).Select(r => r.UserId).ToListAsync(Ct);
    }

    private async Task DeleteAsync(List<Guid> userIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await db.IdempotencyRecords.Where(r => userIds.Contains(r.UserId)).ExecuteDeleteAsync(Ct);
    }
}
