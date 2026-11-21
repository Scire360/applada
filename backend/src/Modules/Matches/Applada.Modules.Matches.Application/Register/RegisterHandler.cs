using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Application.Events;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Application.Queries;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.Register;

/// <summary>
/// Resultado de <c>POST /matches/{id}/registrations</c>. <see cref="Position"/> só em espera (1 = próximo a ser
/// promovido). <see cref="Created"/> é verdadeiro quando a inscrição foi criada ou reativada (201) e falso quando já
/// estava ativa (200).
/// </summary>
public sealed record RegistrationResult(string Status, int? Position, bool Created);

/// <summary>
/// Inscreve o próprio usuário sob o lock da partida (<c>SELECT ... FOR UPDATE</c>): a ordem de chegada à fila é a ordem
/// de aquisição do lock, e capacidade e contadores são decididos pelo domínio com a linha travada.
/// </summary>
public sealed class RegisterHandler(IMatchesUnitOfWork unitOfWork, MatchesMetrics metrics, TimeProvider timeProvider)
{
    public async Task<Result<RegistrationResult>> HandleAsync(Guid matchId, Guid userId, CancellationToken ct)
    {
        var result = await unitOfWork.ExecuteAsync<Registered>(
            async (session, token) =>
            {
                var match = await session.GetMatchForUpdateAsync(matchId, token);
                if (match is null)
                {
                    return MatchErrors.MatchNotFound;
                }

                // Lida depois do lock: enxerga tudo o que a transação anterior nesta partida gravou.
                var existing = await session.FindRegistrationAsync(matchId, userId, token);
                var outcome = match.Register(userId, existing, timeProvider.GetUtcNow());
                if (!outcome.IsSuccess)
                {
                    return outcome.Error;
                }

                var (registration, changed) = outcome.Value;
                if (existing is null)
                {
                    session.AddRegistration(registration);
                }

                int? position = null;
                if (registration.Status == RegistrationStatus.Waitlisted)
                {
                    // Quem acabou de entrar está no fim da fila (maior queue_seq): a posição é o tamanho da espera.
                    // Quem já esperava é contado no banco, sob o mesmo lock.
                    position = changed
                        ? match.WaitlistCount
                        : await session.CountWaitlistedUpToAsync(matchId, registration.QueueSeq, token);
                }

                MatchEventMapper.PublishPending(match, session);
                return new Registered(registration.Status, position, changed);
            },
            ct);

        // Métrica só depois do commit: inscrição desfeita nunca conta.
        if (!result.IsSuccess)
        {
            metrics.Rejected(result.Error);
            return result.Error;
        }

        var (status, position, created) = result.Value;
        metrics.Registered(status, created);
        return new RegistrationResult(StatusText.Of(status), position, created);
    }

    private sealed record Registered(RegistrationStatus Status, int? Position, bool Changed);
}
