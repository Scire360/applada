using System.Text.Json;

namespace Applada.BuildingBlocks.Contracts;

/// <summary>
/// Corpo da mensagem publicada no RabbitMQ. Entrega at-least-once: consumidores deduplicam por
/// <see cref="EventId"/> e não podem assumir ordem entre eventos.
/// </summary>
public sealed record IntegrationEventEnvelope(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    JsonElement Payload);
