using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Application.Events;
using Applada.Modules.Matches.Application.Ports;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.CreateMatch;

/// <summary>
/// Cria a partida com <c>Idempotency-Key</c>: a resposta é gravada junto com a partida e o evento, na mesma transação,
/// e a mesma chave com o mesmo conteúdo devolve exatamente a resposta gravada (status e corpo). Erros de validação não
/// são gravados: corrigir o conteúdo e repetir a chave cria a partida.
/// </summary>
/// <remarks>
/// Corrida da mesma chave: as requisições concorrentes gravam a chave na mesma tabela; a chave primária
/// <c>(user_id, key)</c> deixa uma vencer, e as demais esperam o commit dela, recebem <c>23505</c>
/// (<see cref="IdempotencyConflictException"/>) e releem a resposta gravada uma vez, numa transação nova.
/// Essa espera conta no <c>lock_timeout</c>: se a primeira segurar a transação por mais que ele, a segunda recebe
/// <c>lock-timeout</c> (503 com <c>Retry-After</c>) e o cliente repete. Aceitável: a repetição devolve a resposta gravada.
/// </remarks>
public sealed class CreateMatchHandler(IMatchesUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<Result<(int Status, string Body)>> HandleAsync(
        NewMatch data,
        Guid userId,
        string idempotencyKey,
        string requestHash,
        Func<Match, (int Status, string Body)> render,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        ArgumentException.ThrowIfNullOrEmpty(requestHash);
        ArgumentNullException.ThrowIfNull(render);

        try
        {
            return await unitOfWork.ExecuteAsync<(int Status, string Body)>(
                async (session, token) =>
                {
                    var stored = await session.FindIdempotencyAsync(userId, idempotencyKey, token);
                    if (stored is not null)
                    {
                        return Replay(stored, requestHash);
                    }

                    var created = Match.Create(data, userId, timeProvider.GetUtcNow());
                    if (!created.IsSuccess)
                    {
                        return created.Error;
                    }

                    var match = created.Value;
                    session.AddMatch(match);
                    MatchEventMapper.PublishPending(match, session);

                    var response = render(match);
                    session.AddIdempotency(new IdempotencyEntry(userId, idempotencyKey, requestHash, response.Status, response.Body));
                    return response;
                },
                ct);
        }
        catch (IdempotencyConflictException)
        {
            // Outra requisição com a mesma chave gravou primeiro; a nossa transação já foi desfeita.
            var reread = await unitOfWork.ExecuteAsync<IdempotencyEntry?>(
                async (session, token) => await session.FindIdempotencyAsync(userId, idempotencyKey, token),
                ct);
            if (!reread.IsSuccess)
            {
                return reread.Error;
            }

            return reread.Value is { } stored
                ? Replay(stored, requestHash)
                : throw new InvalidOperationException("Conflito de Idempotency-Key sem registro gravado para reler.");
        }
    }

    private static Result<(int Status, string Body)> Replay(IdempotencyEntry stored, string requestHash) =>
        string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal)
            ? (stored.StatusCode, stored.ResponseBody)
            : MatchErrors.IdempotencyKeyReused;
}
