using System.Text;
using Applada.BuildingBlocks.Messaging;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Identity.Contracts;

namespace Applada.Contracts.Tests;

/// <summary>
/// Congela o formato publicado no RabbitMQ. Mudou o literal: é mudança de contrato e exige nova versão do evento.
/// </summary>
public class IntegrationEventContractTests
{
    [Fact]
    public void UserRegisteredV1_envelope_shape()
    {
        var eventId = Guid.Parse("01930000-0000-7000-8000-000000000001");
        var userId = Guid.Parse("01930000-0000-7000-8000-000000000002");
        var occurredAt = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        var evt = new UserRegisteredV1(userId);

        // Mesmo código da produção: OutboxMessage.From gera o payload e o publicador serializa o envelope com
        // IntegrationEventEnvelopeSerializer. O id fixo da linha do outbox vira o eventId.
        var message = OutboxMessage.From(evt, "c-1", occurredAt, eventId);

        var json = Encoding.UTF8.GetString(IntegrationEventEnvelopeSerializer.Serialize(message));

        const string expected =
            """{"eventId":"01930000-0000-7000-8000-000000000001","eventType":"identity.user-registered.v1","schemaVersion":1,"aggregateId":"01930000-0000-7000-8000-000000000002","occurredAt":"2026-11-13T12:00:00+00:00","correlationId":"c-1","payload":{"userId":"01930000-0000-7000-8000-000000000002"}}""";
        Assert.Equal(expected, json);
    }
}
