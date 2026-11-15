namespace Applada.BuildingBlocks.Contracts;

/// <summary>
/// Nomes das políticas de rate limiting. A raiz de composição (<c>Applada.Api</c>) define as políticas; os módulos só
/// as referenciam pelo nome nas rotas.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Escritas por usuário (POST, PATCH, DELETE): janela fixa de 1 minuto.</summary>
    public const string Writes = "writes";
}
