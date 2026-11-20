using Applada.BuildingBlocks.Contracts;
using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Contracts;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Matches.Domain.Events;

namespace Applada.Modules.Matches.Application.Events;

/// <summary>
/// Converte eventos de domínio em eventos de integração v1. Os textos são fixos aqui, não derivados do nome do membro
/// do enum: renomear um enum do domínio não pode mudar o contrato publicado.
/// </summary>
public static class MatchEventMapper
{
    public static IntegrationEvent Map(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return domainEvent switch
        {
            MatchCreated e => new MatchCreatedV1(e.MatchId, e.OrganizerId, e.StartsAt, e.Capacity, VisibilityText(e.Visibility)),
            SpotConfirmed e => new SpotConfirmedV1(e.MatchId, e.UserId, SourceText(e.Source)),
            SpotReleased e => new SpotReleasedV1(e.MatchId, e.UserId),
            MatchCancelled e => new MatchCancelledV1(e.MatchId),
            _ => throw new ArgumentException($"Evento de domínio sem mapeamento: {domainEvent.GetType().Name}.", nameof(domainEvent)),
        };
    }

    /// <summary>Grava no outbox, na transação da sessão, os eventos pendentes da partida e os descarta dela.</summary>
    public static void PublishPending(Match match, IMatchesSession session)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(session);

        foreach (var domainEvent in match.DomainEvents)
        {
            session.Publish(Map(domainEvent));
        }

        match.ClearDomainEvents();
    }

    private static string VisibilityText(MatchVisibility visibility) => visibility switch
    {
        MatchVisibility.Public => "public",
        _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, "Visibilidade sem texto de contrato."),
    };

    private static string SourceText(ConfirmationSource source) => source switch
    {
        ConfirmationSource.Direct => SpotConfirmedV1.SourceDirect,
        ConfirmationSource.WaitlistPromotion => SpotConfirmedV1.SourceWaitlistPromotion,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Origem sem texto de contrato."),
    };
}
