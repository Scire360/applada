using Applada.BuildingBlocks.Persistence.Outbox;

namespace Applada.BuildingBlocks.Messaging;

/// <summary>
/// Publica uma mensagem do outbox no broker. Só retorna depois da confirmação do broker;
/// recusa (nack) ou timeout viram exceção.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}
