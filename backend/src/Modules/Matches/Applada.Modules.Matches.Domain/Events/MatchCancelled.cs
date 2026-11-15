using Applada.BuildingBlocks.Domain;

namespace Applada.Modules.Matches.Domain.Events;

public sealed record MatchCancelled(Guid MatchId) : IDomainEvent;
