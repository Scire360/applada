namespace Applada.Modules.Profiles.Contracts;

/// <summary>Consulta em lote dos nomes de exibição. O chamador autoriza o recurso antes de consultar seus usuários.</summary>
public interface IProfileNames
{
    /// <summary>Devolve somente os usuários que já têm perfil.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
