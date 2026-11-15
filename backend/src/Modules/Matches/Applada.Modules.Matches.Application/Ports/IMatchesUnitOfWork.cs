using Applada.BuildingBlocks.Domain;

namespace Applada.Modules.Matches.Application.Ports;

/// <summary>
/// Unidade de trabalho das escritas de Matches: uma transação por chamada. Sucesso de <c>work</c> grava tudo
/// (partida, inscrições, idempotência e outbox) e confirma; erro devolvido ou exceção desfazem tudo.
/// </summary>
public interface IMatchesUnitOfWork
{
    /// <summary>
    /// Roda <paramref name="work"/> numa transação com <c>lock_timeout</c>. Lock não obtido no prazo vira o erro
    /// <c>lock-timeout</c>; chave de idempotência gravada antes por outra requisição lança
    /// <see cref="IdempotencyConflictException"/>. Demais exceções propagam (sempre com rollback).
    /// </summary>
    Task<Result<T>> ExecuteAsync<T>(Func<IMatchesSession, CancellationToken, Task<Result<T>>> work, CancellationToken ct);
}
