using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Messaging;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Identity.Contracts;
using Applada.Modules.Matches.Contracts;

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

    [Fact]
    public void All_integration_event_types_are_unique_and_versioned()
    {
        // Referências de tipo forçam o carregamento dos dois assemblies de contratos antes da reflexão.
        Assembly[] contractAssemblies = [typeof(UserRegisteredV1).Assembly, typeof(MatchCreatedV1).Assembly];

        var eventTypes = contractAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(IntegrationEvent).IsAssignableFrom(t))
            .ToList();

        Assert.Contains(typeof(UserRegisteredV1), eventTypes);
        Assert.Contains(typeof(MatchCreatedV1), eventTypes);
        Assert.Contains(typeof(SpotConfirmedV1), eventTypes);
        Assert.Contains(typeof(SpotReleasedV1), eventTypes);
        Assert.Contains(typeof(MatchCancelledV1), eventTypes);

        var pattern = new Regex(@"^[a-z]+\.[a-z-]+\.v\d+$");
        var seen = new Dictionary<string, Type>();
        foreach (var type in eventTypes)
        {
            // Os records têm parâmetros de construtor; EventType e SchemaVersion são constantes (não leem estado),
            // então uma instância não inicializada basta e não depende de valores de exemplo por tipo.
            var evt = (IntegrationEvent)RuntimeHelpers.GetUninitializedObject(type);

            Assert.Matches(pattern, evt.EventType);
            Assert.EndsWith($".v{evt.SchemaVersion}", evt.EventType, StringComparison.Ordinal);
            Assert.True(seen.TryAdd(evt.EventType, type), $"EventType '{evt.EventType}' repetido em {type.Name} e {seen.GetValueOrDefault(evt.EventType)?.Name}.");
        }
    }
}
