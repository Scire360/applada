namespace Applada.BuildingBlocks.Contracts;

/// <summary>
/// Usuário interno da requisição corrente, resolvido a partir do par (iss, sub) do token.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Id interno (UUIDv7) do usuário.
    /// </summary>
    /// <exception cref="InvalidOperationException">Nenhum usuário foi resolvido nesta requisição.</exception>
    Guid UserId { get; }
}
