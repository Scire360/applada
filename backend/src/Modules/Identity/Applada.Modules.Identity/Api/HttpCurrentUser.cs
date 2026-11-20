using Applada.BuildingBlocks.Contracts;

namespace Applada.Modules.Identity.Api;

/// <summary>
/// <see cref="ICurrentUser"/> da requisição HTTP (escopo por requisição), preenchido pelo
/// <see cref="CurrentUserMiddleware"/>.
/// </summary>
internal sealed class HttpCurrentUser : ICurrentUser
{
    private Guid? _userId;

    public Guid UserId => _userId
        ?? throw new InvalidOperationException("Nenhum usuário autenticado foi resolvido nesta requisição.");

    internal void Resolve(Guid userId)
    {
        if (_userId is not null)
        {
            throw new InvalidOperationException("O usuário da requisição já foi resolvido.");
        }

        _userId = userId;
    }
}
