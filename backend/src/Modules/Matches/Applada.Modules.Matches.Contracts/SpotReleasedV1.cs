using Applada.BuildingBlocks.Contracts;

namespace Applada.Modules.Matches.Contracts;

/// <summary>Um confirmado saiu e liberou a vaga (a promoção da espera, se houver, vem num <see cref="SpotConfirmedV1"/>).</summary>
public sealed record SpotReleasedV1(Guid MatchId, Guid UserId) : IntegrationEvent
{
    public const string Type = "matches.spot-released.v1";

    public override string EventType => Type;

    public override int SchemaVersion => 1;

    public override Guid AggregateId => MatchId;
}
