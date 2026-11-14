namespace Applada.Modules.Matches.Infrastructure;

/// <summary>Opções de persistência de Matches (seção <c>Matches</c>).</summary>
public sealed class MatchesPersistenceOptions
{
    public const string SectionName = "Matches";

    /// <summary>
    /// Espera máxima pelo lock da partida (<c>lock_timeout</c> local à transação). Estourou → <c>lock-timeout</c> (503).
    /// Precisão de milissegundos; mínimo 1 ms.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
