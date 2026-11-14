using Applada.BuildingBlocks.Domain;

namespace Applada.Modules.Matches.Domain.Events;

public sealed record MatchCreated(Guid MatchId, Guid OrganizerId, DateTimeOffset StartsAt, int Capacity, MatchVisibility Visibility) : IDomainEvent;
