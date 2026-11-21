using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain;

namespace Applada.Modules.Matches.Application.Queries;

/// <summary>Uma página de uma listagem. <see cref="NextCursor"/> nulo: não há próxima página.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Tamanho de página das listagens por cursor.</summary>
public static class PageLimit
{
    public const int Default = 20;
    public const int Max = 50;

    /// <summary>Código <c>validation</c> no campo <c>limit</c>: fora de 1..<see cref="Max"/> ou não numérico.</summary>
    public static Error Invalid { get; } = MatchErrors.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["limit"] = [$"O limite deve ser um inteiro entre 1 e {Max}."],
    });

    public static bool IsValid(int limit) => limit is >= 1 and <= Max;
}
