using Applada.BuildingBlocks.Contracts;

namespace Applada.Modules.Identity.Contracts;

/// <summary>
/// Usuário interno criado no primeiro acesso. Payload: <c>{ "userId": "..." }</c>, sem PII.
/// </summary>
public sealed record UserRegisteredV1(Guid UserId) : IntegrationEvent
{
    public const string Type = "identity.user-registered.v1";

    public override string EventType => Type;

    public override int SchemaVersion => 1;

    public override Guid AggregateId => UserId;
}
