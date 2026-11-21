namespace Applada.Modules.Communities.Contracts;

/// <summary>Papel de um usuário num grupo.</summary>
public enum GroupRole
{
    Owner,
    Admin,
    Member,
}

/// <summary>Consulta síncrona de participação em grupo, para outros módulos autorizarem ações ligadas a um grupo.</summary>
public interface IGroupMembership
{
    /// <summary>Papel de <paramref name="userId"/> em <paramref name="groupId"/>; nulo se o grupo não existe ou o usuário não é membro.</summary>
    Task<GroupRole?> GetRoleAsync(Guid groupId, Guid userId, CancellationToken ct);
}
