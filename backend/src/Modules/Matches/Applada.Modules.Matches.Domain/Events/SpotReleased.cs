using Applada.BuildingBlocks.Domain;

namespace Applada.Modules.Matches.Domain.Events;

public sealed record SpotReleased(Guid MatchId, Guid UserId) : IDomainEvent;
