using Applada.BuildingBlocks.Domain;

namespace Applada.Modules.Matches.Domain.Events;

public sealed record SpotConfirmed(Guid MatchId, Guid UserId, ConfirmationSource Source) : IDomainEvent;
