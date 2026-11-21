using Microsoft.EntityFrameworkCore;

namespace Applada.BuildingBlocks.Persistence.Outbox;

public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Mapeia <see cref="OutboxMessage"/> na tabela <c>outbox_messages</c> do schema padrão do contexto,
    /// com índice parcial <c>(next_attempt_at) WHERE published_at IS NULL</c> para a reserva de lote.
    /// </summary>
    public static ModelBuilder AddOutboxMessages(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("outbox_messages");
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Id).ValueGeneratedNever();
            outbox.Property(m => m.EventType).HasColumnType("text").IsRequired();
            outbox.Property(m => m.SchemaVersion).IsRequired();
            outbox.Property(m => m.AggregateId).IsRequired();
            outbox.Property(m => m.OccurredAt).HasColumnType("timestamptz").IsRequired();
            outbox.Property(m => m.CorrelationId).HasColumnType("text").IsRequired();
            outbox.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
            outbox.Property(m => m.PublishedAt).HasColumnType("timestamptz");
            outbox.Property(m => m.Attempts).IsRequired();
            outbox.Property(m => m.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);
            outbox.Property(m => m.NextAttemptAt).HasColumnType("timestamptz").IsRequired();
            outbox.HasIndex(m => m.NextAttemptAt).HasFilter("published_at IS NULL");
        });

        return modelBuilder;
    }
}
