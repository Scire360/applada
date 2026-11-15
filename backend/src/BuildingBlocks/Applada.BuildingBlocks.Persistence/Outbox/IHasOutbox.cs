using Microsoft.EntityFrameworkCore;

namespace Applada.BuildingBlocks.Persistence.Outbox;

/// <summary>
/// <c>DbContext</c> de módulo que tem outbox próprio (tabela <c>&lt;schema&gt;.outbox_messages</c>).
/// </summary>
public interface IHasOutbox
{
    DbSet<OutboxMessage> OutboxMessages { get; }

    /// <summary>
    /// SQL que reserva o próximo lote: pendentes com <c>next_attempt_at &lt;= now</c>, em ordem de
    /// <c>occurred_at</c>, com <c>FOR UPDATE SKIP LOCKED</c>. Cada módulo escreve o schema literal;
    /// <paramref name="now"/> e <paramref name="batchSize"/> viram parâmetros.
    /// </summary>
    FormattableString ClaimBatchSql(DateTimeOffset now, int batchSize);
}
