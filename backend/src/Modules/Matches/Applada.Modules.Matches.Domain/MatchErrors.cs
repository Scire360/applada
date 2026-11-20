using Applada.BuildingBlocks.Domain;

namespace Applada.Modules.Matches.Domain;

/// <summary>
/// Um <see cref="Error"/> por código da tabela fixa de erros do módulo. O código vira o <c>type</c> do
/// problem+json (<c>urn:applada:problem:&lt;code&gt;</c>); a mensagem vai ao cliente, então não leva detalhe interno.
/// </summary>
public static class MatchErrors
{
    public static Error InvalidCursor { get; } =
        new("invalid-cursor", ErrorKind.Validation, "O cursor de paginação é inválido.");

    public static Error IdempotencyKeyMissing { get; } =
        new("idempotency-key-missing", ErrorKind.Validation, "Informe um cabeçalho Idempotency-Key válido.");

    public static Error NotOrganizer { get; } =
        new("not-organizer", ErrorKind.Forbidden, "Só o organizador pode alterar a partida.");

    public static Error ParticipantsForbidden { get; } =
        new("participants-forbidden", ErrorKind.Forbidden, "A lista de participantes é visível só ao organizador e aos inscritos.");

    public static Error MatchNotFound { get; } =
        new("match-not-found", ErrorKind.NotFound, "Partida não encontrada.");

    public static Error RegistrationNotFound { get; } =
        new("registration-not-found", ErrorKind.NotFound, "Não há inscrição ativa nesta partida.");

    public static Error RegistrationClosed { get; } =
        new("registration-closed", ErrorKind.Conflict, "A partida já começou; não aceita mais alterações.");

    public static Error MatchCancelled { get; } =
        new("match-cancelled", ErrorKind.Conflict, "A partida foi cancelada.");

    public static Error CapacityBelowConfirmed { get; } =
        new("capacity-below-confirmed", ErrorKind.Conflict, "A capacidade não pode ficar abaixo do número de confirmados.");

    public static Error WaitlistFull { get; } =
        new("waitlist-full", ErrorKind.Conflict, "A lista de espera está cheia.");

    public static Error VersionMismatch { get; } =
        new("version-mismatch", ErrorKind.PreconditionFailed, "A partida foi alterada por outra requisição; recarregue e tente de novo.");

    public static Error IfMatchRequired { get; } =
        new("if-match-required", ErrorKind.PreconditionFailed, "Informe o cabeçalho If-Match com a versão atual da partida.");

    public static Error IdempotencyKeyReused { get; } =
        new("idempotency-key-reused", ErrorKind.Unprocessable, "Esta Idempotency-Key já foi usada com outro conteúdo.");

    /// <summary>Grupo inexistente ou o organizador não é owner/admin dele: a mesma resposta, para não revelar o grupo.</summary>
    public static Error GroupNotAllowed { get; } =
        new("group-not-allowed", ErrorKind.Unprocessable, "Grupo inexistente ou você não pode organizar partidas nele.");

    public static Error LockTimeout { get; } =
        new("lock-timeout", ErrorKind.Unavailable, "A partida está ocupada no momento; tente de novo em instantes.");

    /// <summary>Código <c>validation</c>, com as mensagens por campo (chaves em camelCase, como no contrato HTTP).</summary>
    public static Error Validation(IReadOnlyDictionary<string, string[]> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new Error("validation", ErrorKind.Validation, "Um ou mais campos são inválidos.", fields);
    }
}
