using Applada.BuildingBlocks.Contracts;

namespace Applada.Modules.Matches.Contracts;

/// <summary>Partida criada. <see cref="Visibility"/> é texto estável (<c>public</c> nesta versão), não o enum do domínio.</summary>
public sealed record MatchCreatedV1(Guid MatchId, Guid OrganizerId, DateTimeOffset StartsAt, int Capacity, string Visibility) : IntegrationEvent
{
    public const string Type = "matches.match-created.v1";

    public override string EventType => Type;

    public override int SchemaVersion => 1;

    public override Guid AggregateId => MatchId;
}
