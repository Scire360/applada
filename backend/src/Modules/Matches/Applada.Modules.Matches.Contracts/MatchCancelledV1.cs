using Applada.BuildingBlocks.Contracts;

namespace Applada.Modules.Matches.Contracts;

/// <summary>Partida cancelada pelo organizador; as inscrições ficam como histórico.</summary>
public sealed record MatchCancelledV1(Guid MatchId) : IntegrationEvent
{
    public const string Type = "matches.match-cancelled.v1";

    public override string EventType => Type;

    public override int SchemaVersion => 1;

    public override Guid AggregateId => MatchId;
}
