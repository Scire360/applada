using Microsoft.EntityFrameworkCore;

namespace Applada.BuildingBlocks.Persistence.Idempotency;

public static class IdempotencyModelBuilderExtensions
{
    /// <summary>
    /// Nome da chave primária de <c>idempotency_keys</c>. Violação (<c>23505</c>) nela significa que outra
    /// requisição com a mesma chave já gravou primeiro.
    /// </summary>
    public const string PrimaryKeyName = "pk_idempotency_keys";

    /// <summary>
    /// Mapeia <see cref="IdempotencyRecord"/> na tabela <c>idempotency_keys</c> do schema padrão do contexto,
    /// com chave primária <c>(user_id, key)</c> e índice em <c>created_at</c> para a limpeza por retenção.
    /// </summary>
    public static ModelBuilder AddIdempotencyRecords(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<IdempotencyRecord>(record =>
        {
            record.ToTable("idempotency_keys");
            record.HasKey(r => new { r.UserId, r.Key }).HasName(PrimaryKeyName);
            record.Property(r => r.Key).HasColumnType("text").IsRequired();
            record.Property(r => r.RequestHash).HasColumnType("text").IsRequired();
            record.Property(r => r.StatusCode).IsRequired();
            record.Property(r => r.ResponseBody).HasColumnType("text").IsRequired();
            record.Property(r => r.CreatedAt).HasColumnType("timestamptz").IsRequired();
            record.HasIndex(r => r.CreatedAt);
        });

        return modelBuilder;
    }
}
