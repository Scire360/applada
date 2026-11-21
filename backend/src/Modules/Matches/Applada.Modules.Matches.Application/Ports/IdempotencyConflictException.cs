namespace Applada.Modules.Matches.Application.Ports;

/// <summary>
/// Outra requisição gravou a mesma <c>Idempotency-Key</c> primeiro (violação da chave primária de
/// <c>idempotency_keys</c>). A transação já foi desfeita; o handler relê a resposta guardada.
/// </summary>
public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException()
        : base("A Idempotency-Key foi gravada por outra requisição.")
    {
    }

    public IdempotencyConflictException(string message)
        : base(message)
    {
    }

    public IdempotencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
