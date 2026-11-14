using Applada.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Applada.BuildingBlocks.Messaging;

/// <summary>
/// Publica um lote do outbox de <typeparamref name="TContext"/>. Numa única transação: reserva as
/// mensagens vencidas com <c>FOR UPDATE SKIP LOCKED</c>, publica cada uma com confirmação do broker e
/// grava o resultado (publicada ou próxima tentativa) antes do commit. Processadores concorrentes, na
/// mesma réplica ou em outras, nunca reservam a mesma linha.
/// </summary>
/// <remarks>
/// Entrega at-least-once: se o commit falhar depois da confirmação do broker, a mensagem volta a ser
/// publicada num próximo lote. Consumidores deduplicam por <c>eventId</c>.
/// </remarks>
public sealed partial class OutboxProcessor<TContext>(
    IServiceScopeFactory scopeFactory,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider,
    IOptions<OutboxOptions> options,
    OutboxMetrics metrics,
    ILogger<OutboxProcessor<TContext>> logger)
    where TContext : DbContext, IHasOutbox
{
    private static readonly string OutboxName = typeof(TContext).Name;

    private readonly OutboxOptions _options = options.Value;

    /// <summary>Processa um lote e devolve quantas mensagens foram publicadas.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var published = await PublishBatchAsync(db, ct);
        await DeleteExpiredAsync(db, ct);
        metrics.Pending(OutboxName, await db.OutboxMessages.LongCountAsync(m => m.PublishedAt == null, ct));

        return published;
    }

    private async Task<int> PublishBatchAsync(TContext db, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var batch = await db.OutboxMessages.FromSql(db.ClaimBatchSql(now, _options.BatchSize)).ToListAsync(ct);
        var published = 0;

        foreach (var message in batch)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await publisher.PublishAsync(message, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Desligando: esta mensagem fica pendente (pode ter chegado ao broker: at-least-once).
                break;
            }
            catch (Exception ex)
            {
                var nextAttemptAt = timeProvider.GetUtcNow() + Backoff(message.Attempts + 1);
                // Só tipo e mensagem da exceção, nunca o payload.
                message.RecordFailure($"{ex.GetType().FullName}: {ex.Message}", nextAttemptAt);
                metrics.PublishFailed(OutboxName);
                LogPublishFailed(logger, message.Id, message.EventType, message.Attempts, nextAttemptAt, ex);
                continue;
            }

            var publishedAt = timeProvider.GetUtcNow();
            message.MarkPublished(publishedAt);
            metrics.Published(OutboxName, publishedAt - message.OccurredAt);
            published++;
        }

        // Grava o que já foi confirmado mesmo se o desligamento interrompeu o lote: evita republicar.
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        return published;
    }

    private async Task DeleteExpiredAsync(TContext db, CancellationToken ct)
    {
        var cutoff = timeProvider.GetUtcNow() - _options.Retention;
        var deleted = await db.OutboxMessages
            .Where(m => m.PublishedAt != null && m.PublishedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            LogDeleted(logger, deleted, OutboxName);
        }
    }

    /// <summary><c>min(2^attempts s, MaxBackoff)</c>.</summary>
    private TimeSpan Backoff(int attempts)
    {
        var seconds = Math.Pow(2, attempts);
        return seconds >= _options.MaxBackoff.TotalSeconds ? _options.MaxBackoff : TimeSpan.FromSeconds(seconds);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao publicar a mensagem {MessageId} ({EventType}) do outbox; tentativa {Attempts}, próxima em {NextAttemptAt}.")]
    private static partial void LogPublishFailed(ILogger logger, Guid messageId, string eventType, int attempts, DateTimeOffset nextAttemptAt, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} mensagens publicadas além da retenção apagadas do outbox {Outbox}.")]
    private static partial void LogDeleted(ILogger logger, int count, string outbox);
}
