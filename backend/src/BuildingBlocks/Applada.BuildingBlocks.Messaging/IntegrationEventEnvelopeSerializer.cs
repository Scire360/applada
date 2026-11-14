using System.Text.Json;
using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Persistence.Outbox;

namespace Applada.BuildingBlocks.Messaging;

/// <summary>
/// Monta e serializa o envelope publicado no RabbitMQ a partir de uma linha do outbox. Fica separado do
/// publicador para o teste de contrato exercitar exatamente o mesmo código.
/// </summary>
internal static class IntegrationEventEnvelopeSerializer
{
    public static byte[] Serialize(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var payload = JsonDocument.Parse(message.Payload);
        var envelope = new IntegrationEventEnvelope(
            message.Id,
            message.EventType,
            message.SchemaVersion,
            message.AggregateId,
            message.OccurredAt,
            message.CorrelationId,
            payload.RootElement);
        return JsonSerializer.SerializeToUtf8Bytes(envelope, IntegrationEventJson.Options);
    }
}
