using System.Text.Json.Serialization;

namespace Applada.BuildingBlocks.Contracts;

/// <summary>
/// Evento de integração publicado por um módulo via outbox. As propriedades de metadados vão no
/// envelope (<see cref="IntegrationEventEnvelope"/>), não no payload: por isso são ignoradas na serialização.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>Tipo do evento, já com a versão (ex.: <c>identity.user-registered.v1</c>). Também é a routing key.</summary>
    [JsonIgnore]
    public abstract string EventType { get; }

    [JsonIgnore]
    public abstract int SchemaVersion { get; }

    /// <summary>Id do agregado que originou o evento.</summary>
    [JsonIgnore]
    public abstract Guid AggregateId { get; }
}
