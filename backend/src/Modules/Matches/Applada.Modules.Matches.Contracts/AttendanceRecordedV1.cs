using Applada.BuildingBlocks.Contracts;

namespace Applada.Modules.Matches.Contracts;

public sealed record AttendanceRecordedV1(Guid MatchId, Guid UserId, string Status, DateTimeOffset RecordedAt) : IntegrationEvent
{
    public const string Type = "matches.attendance-recorded.v1";

    public override string EventType => Type;

    public override int SchemaVersion => 1;

    public override Guid AggregateId => MatchId;
}
