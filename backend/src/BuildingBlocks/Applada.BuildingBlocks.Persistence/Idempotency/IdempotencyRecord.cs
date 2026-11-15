namespace Applada.BuildingBlocks.Persistence.Idempotency;

/// <summary>
/// Resposta já dada a uma escrita com <c>Idempotency-Key</c>, gravada na mesma transação da operação.
/// A chave primária <c>(user_id, key)</c> decide, no banco, qual requisição concorrente vence.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>
    /// Validade de uma resposta guardada (spec §8). Mais antiga que isso, a chave conta como nunca usada: a próxima
    /// requisição com ela é tratada como nova e regrava a linha.
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public required Guid UserId { get; init; }

    public required string Key { get; init; }

    /// <summary>Hash do conteúdo da requisição: mesma chave com outro conteúdo é reuso indevido.</summary>
    public required string RequestHash { get; init; }

    public required int StatusCode { get; init; }

    public required string ResponseBody { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
