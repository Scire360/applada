namespace Applada.Modules.Matches.Infrastructure;

/// <summary>Opções de persistência de Matches (seção <c>Matches</c>).</summary>
public sealed class MatchesPersistenceOptions
{
    public const string SectionName = "Matches";

    /// <summary>Menor <see cref="LockTimeout"/> aceito.</summary>
    public static readonly TimeSpan MinLockTimeout = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Maior <see cref="LockTimeout"/> aceito. Acima disso uma requisição prenderia conexão e thread esperando o lock
    /// por tempo demais; o cliente recebe 503 com <c>Retry-After</c> e tenta de novo.
    /// </summary>
    public static readonly TimeSpan MaxLockTimeout = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Espera máxima pelo lock da partida (<c>lock_timeout</c> local à transação). Estourou → <c>lock-timeout</c> (503).
    /// Precisão de milissegundos; entre 1 ms e 1 minuto.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
