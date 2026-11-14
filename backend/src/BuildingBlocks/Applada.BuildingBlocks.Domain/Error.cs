namespace Applada.BuildingBlocks.Domain;

/// <summary>Categoria do erro: a API traduz cada uma para um status HTTP.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
    PreconditionFailed,
    Unprocessable,
    Unavailable,
}

/// <summary>
/// Erro de regra de negócio, esperado e sem detalhe interno. <see cref="Code"/> é estável (vira o
/// <c>type</c> do problem+json); <see cref="Fields"/> só aparece em validação, com mensagens por campo.
/// </summary>
#pragma warning disable CA1716 // Nome fixado pelo contrato do domínio; o código é só C#.
public sealed record Error(string Code, ErrorKind Kind, string Message, IReadOnlyDictionary<string, string[]>? Fields = null);
#pragma warning restore CA1716
