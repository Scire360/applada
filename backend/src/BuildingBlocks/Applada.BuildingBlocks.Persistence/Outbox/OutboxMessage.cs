using System.Text.Json;
using Applada.BuildingBlocks.Contracts;

namespace Applada.BuildingBlocks.Persistence.Outbox;

/// <summary>
/// Linha do outbox transacional: gravada na mesma transação da mudança de estado que originou o
/// evento e publicada depois pelo <c>OutboxProcessor</c>. <see cref="Id"/> é o <c>eventId</c> do envelope.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Tamanho máximo de <see cref="LastError"/>.</summary>
    public const int MaxErrorLength = 2000;

    private OutboxMessage(
        Guid id,
        string eventType,
        int schemaVersion,
        Guid aggregateId,
        DateTimeOffset occurredAt,
        string correlationId,
        string payload,
        DateTimeOffset nextAttemptAt)
    {
        Id = id;
        EventType = eventType;
        SchemaVersion = schemaVersion;
        AggregateId = aggregateId;
        OccurredAt = occurredAt;
        CorrelationId = correlationId;
        Payload = payload;
        NextAttemptAt = nextAttemptAt;
    }

    public Guid Id { get; private set; }

    public string EventType { get; private set; }

    public int SchemaVersion { get; private set; }

    public Guid AggregateId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string CorrelationId { get; private set; }

    /// <summary>Evento serializado com <see cref="IntegrationEventJson.Options"/> (coluna jsonb).</summary>
    public string Payload { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>Tipo e mensagem da última falha de publicação (nunca o payload), truncados.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public static OutboxMessage From(IntegrationEvent evt, string correlationId, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        // Tipo de runtime: serializa as propriedades do evento concreto, não só as da base.
        var payload = JsonSerializer.Serialize(evt, evt.GetType(), IntegrationEventJson.Options);

        return new OutboxMessage(
            Guid.CreateVersion7(occurredAt),
            evt.EventType,
            evt.SchemaVersion,
            evt.AggregateId,
            occurredAt,
            correlationId,
            payload,
            nextAttemptAt: occurredAt);
    }

    public void MarkPublished(DateTimeOffset publishedAt) => PublishedAt = publishedAt;

    /// <summary>
    /// Registra uma tentativa que falhou e agenda a próxima. <paramref name="error"/> não pode conter o payload.
    /// </summary>
    public void RecordFailure(string error, DateTimeOffset nextAttemptAt)
    {
        ArgumentNullException.ThrowIfNull(error);

        Attempts++;
        LastError = Truncate(error);
        NextAttemptAt = nextAttemptAt;
    }

    /// <summary>Corta em <see cref="MaxErrorLength"/> sem partir um par substituto (UTF-16) ao meio.</summary>
    private static string Truncate(string error)
    {
        if (error.Length <= MaxErrorLength)
        {
            return error;
        }

        var length = char.IsHighSurrogate(error[MaxErrorLength - 1]) ? MaxErrorLength - 1 : MaxErrorLength;
        return error[..length];
    }
}
