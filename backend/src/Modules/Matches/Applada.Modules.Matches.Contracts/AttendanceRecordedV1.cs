using Applada.BuildingBlocks.Contracts;

namespace Applada.Modules.Matches.Contracts;

/// <summary>
/// Eventos do mesmo <c>(MatchId, UserId)</c> podem chegar fora de ordem. Como o V1 não tem sequência,
/// o consumidor deve reconciliar a presença pelo estado atual da API.
/// </summary>
public sealed record AttendanceRecordedV1(Guid MatchId, Guid UserId, string Status, DateTimeOffset RecordedAt) : IntegrationEvent
{
    public const string Type = "matches.attendance-recorded.v1";

    public override string EventType => Type;

    public override int SchemaVersion => 1;

    public override Guid AggregateId => MatchId;
}
