using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Application.Events;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.UpdateMatch;

/// <summary>
/// Edição do organizador sob o lock da partida. Aumentar a capacidade promove a espera na mesma transação; as promoções
/// entram na métrica <c>matches.registrations</c> (<c>promoted</c>) depois do commit.
/// </summary>
public sealed class UpdateMatchHandler(IMatchesUnitOfWork unitOfWork, IMatchQueries queries, MatchesMetrics metrics, TimeProvider timeProvider)
{
    /// <exception cref="MatchStateDivergenceException">
    /// Contadores da partida divergem das linhas de inscrição (bug ou alteração fora da aplicação). Não é mascarado:
    /// a transação é desfeita e o erro sobe.
    /// </exception>
    public async Task<Result<MatchDetails>> HandleAsync(
        Guid matchId,
        Guid userId,
        int expectedVersion,
        string? title,
        string? locationText,
        int? capacity,
        CancellationToken ct)
    {
        var updated = await unitOfWork.ExecuteAsync<int>(
            async (session, token) =>
            {
                var match = await session.GetMatchForUpdateAsync(matchId, token);
                if (match is null)
                {
                    return MatchErrors.MatchNotFound;
                }

                // Saída antecipada: quem não organiza não lê inscrições. O domínio continua sendo a regra (ChangeDetails).
                if (match.OrganizerId != userId)
                {
                    return MatchErrors.NotOrganizer;
                }

                IReadOnlyList<Registration> waitlistHead = [];
                if (capacity is { } newCapacity && newCapacity > match.Capacity)
                {
                    // Só quem a nova capacidade promove; nunca uma consulta com LIMIT negativo ou zero.
                    var count = Math.Max(0, Math.Min(newCapacity - match.ConfirmedCount, match.WaitlistCount));
                    if (count > 0)
                    {
                        waitlistHead = await session.GetWaitlistHeadAsync(matchId, count, token);
                    }
                }

                var changed = match.ChangeDetails(userId, expectedVersion, title, locationText, capacity, waitlistHead, timeProvider.GetUtcNow());
                if (!changed.IsSuccess)
                {
                    return changed.Error;
                }

                var promotions = MatchesMetrics.PendingPromotions(match);
                MatchEventMapper.PublishPending(match, session);
                return promotions;
            },
            ct);

        if (!updated.IsSuccess)
        {
            return updated.Error;
        }

        metrics.Promoted(updated.Value);

        // Lida depois do commit, pelo mesmo caminho do GET: corpo e ETag vêm da mesma leitura.
        return await queries.GetAsync(matchId, userId, ct) is { } details ? details : MatchErrors.MatchNotFound;
    }
}
