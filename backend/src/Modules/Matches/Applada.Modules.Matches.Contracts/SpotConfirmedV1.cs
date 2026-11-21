using Applada.BuildingBlocks.Contracts;

namespace Applada.Modules.Matches.Contracts;

/// <summary>
/// Vaga confirmada para <see cref="UserId"/>: única fonte de "vaga confirmada" para consumidores.
/// <see cref="Source"/> é <see cref="SourceDirect"/> (inscrição com vaga livre) ou <see cref="SourceWaitlistPromotion"/>.
/// </summary>
public sealed record SpotConfirmedV1(Guid MatchId, Guid UserId, string Source) : IntegrationEvent
{
    public const string Type = "matches.spot-confirmed.v1";

    public const string SourceDirect = "direct";

    public const string SourceWaitlistPromotion = "waitlist_promotion";

    public override string EventType => Type;

    public override int SchemaVersion => 1;

    public override Guid AggregateId => MatchId;
}
