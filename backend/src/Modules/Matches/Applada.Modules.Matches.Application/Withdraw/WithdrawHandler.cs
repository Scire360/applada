using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Application.Events;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.Withdraw;

/// <summary>
/// Cancela a inscrição do próprio usuário sob o lock da partida. Se ela estava confirmada, o primeiro da espera é
/// promovido na mesma transação (<c>spot-released</c> + <c>spot-confirmed</c> com <c>waitlist_promotion</c>).
/// </summary>
public sealed class WithdrawHandler(IMatchesUnitOfWork unitOfWork, MatchesMetrics metrics, TimeProvider timeProvider)
{
    /// <exception cref="MatchStateDivergenceException">
    /// O contador diz que há espera, mas não há linha em espera (bug ou alteração fora da aplicação). Não é mascarado:
    /// a transação é desfeita e o erro sobe.
    /// </exception>
    public async Task<Result> HandleAsync(Guid matchId, Guid userId, CancellationToken ct)
    {
        var result = await unitOfWork.ExecuteAsync<int>(
            async (session, token) =>
            {
                var match = await session.GetMatchForUpdateAsync(matchId, token);
                if (match is null)
                {
                    return MatchErrors.MatchNotFound;
                }

                // Sem linha nenhuma, o domínio decide do mesmo jeito (cancelada → fechada → registration-not-found):
                // a ordem das regras fica num lugar só.
                var registration = await session.FindRegistrationAsync(matchId, userId, token);

                // Só uma saída de confirmado promove: só então a cabeça da fila é lida.
                Registration? nextInLine = null;
                if (registration?.Status == RegistrationStatus.Confirmed)
                {
                    var head = await session.GetWaitlistHeadAsync(matchId, 1, token);
                    nextInLine = head.Count > 0 ? head[0] : null;
                }

                var withdrawn = match.Withdraw(registration, nextInLine, timeProvider.GetUtcNow());
                if (!withdrawn.IsSuccess)
                {
                    return withdrawn.Error;
                }

                var promotions = MatchesMetrics.PendingPromotions(match);
                MatchEventMapper.PublishPending(match, session);
                return promotions;
            },
            ct);

        if (!result.IsSuccess)
        {
            metrics.Rejected(result.Error);
            return result.Error;
        }

        metrics.Withdrawn();
        metrics.Promoted(result.Value);
        return Result.Success();
    }
}
